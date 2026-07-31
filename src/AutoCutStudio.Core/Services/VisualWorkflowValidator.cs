using AutoCutStudio.Core.Models;

namespace AutoCutStudio.Core.Services;

public sealed class VisualWorkflowValidator
{
    public WorkflowValidationResult Validate(VisualWorkflowDocument workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var errors = new List<string>();
        var warnings = new List<string>();
        var nodes = workflow.Nodes.ToDictionary(node => node.NodeId);

        if (workflow.Nodes.Count == 0)
        {
            errors.Add("Workflow ต้องมีอย่างน้อย 1 Node");
        }

        if (nodes.Count != workflow.Nodes.Count)
        {
            errors.Add("พบ Node ID ซ้ำใน Workflow");
        }

        foreach (var node in workflow.Nodes)
        {
            if (!VisualNodeTypes.All.Contains(node.NodeType))
            {
                errors.Add($"Node ‘{node.DisplayName}’ ใช้ประเภทที่ไม่รองรับ: {node.NodeType}");
            }
        }

        var connectionIds = new HashSet<Guid>();
        foreach (var connection in workflow.Connections)
        {
            if (!connectionIds.Add(connection.ConnectionId))
            {
                errors.Add($"พบ Connection ID ซ้ำ: {connection.ConnectionId:N}");
            }

            if (!nodes.ContainsKey(connection.SourceNodeId))
            {
                errors.Add("Connection อ้างถึง Source Node ที่ไม่มีอยู่");
            }

            if (!nodes.ContainsKey(connection.TargetNodeId))
            {
                errors.Add("Connection อ้างถึง Target Node ที่ไม่มีอยู่");
            }

            if (connection.SourceNodeId == connection.TargetNodeId)
            {
                errors.Add("Node ไม่สามารถเชื่อมกลับเข้าตัวเองได้");
            }
        }

        if (errors.Count > 0)
        {
            return new WorkflowValidationResult { Errors = errors, Warnings = warnings };
        }

        var incoming = workflow.Nodes.ToDictionary(node => node.NodeId, _ => 0);
        var outgoing = workflow.Nodes.ToDictionary(node => node.NodeId, _ => new List<Guid>());
        foreach (var connection in workflow.Connections.DistinctBy(item => new { item.SourceNodeId, item.TargetNodeId }))
        {
            incoming[connection.TargetNodeId]++;
            outgoing[connection.SourceNodeId].Add(connection.TargetNodeId);
        }

        var queue = new PriorityQueue<Guid, (double X, double Y, string Id)>();
        foreach (var node in workflow.Nodes.Where(node => incoming[node.NodeId] == 0))
        {
            queue.Enqueue(node.NodeId, (node.X, node.Y, node.NodeId.ToString("N")));
        }

        var order = new List<Guid>();
        while (queue.TryDequeue(out var nodeId, out _))
        {
            order.Add(nodeId);
            foreach (var target in outgoing[nodeId])
            {
                incoming[target]--;
                if (incoming[target] == 0)
                {
                    var node = nodes[target];
                    queue.Enqueue(target, (node.X, node.Y, node.NodeId.ToString("N")));
                }
            }
        }

        if (order.Count != workflow.Nodes.Count)
        {
            errors.Add("Workflow มีวงจรเชื่อมต่อ จึงไม่สามารถกำหนดลำดับการทำงานได้");
        }

        var enabledNodes = workflow.Nodes.Where(node => !node.IsDisabled).ToList();
        if (!enabledNodes.Any(node => node.NodeType is VisualNodeTypes.MediaInput or VisualNodeTypes.FolderInput))
        {
            warnings.Add("Workflow ไม่มี Input Node ที่เปิดใช้งาน ระบบจะใช้ Media ที่กำลังเลือกอยู่");
        }

        if (!enabledNodes.Any(node => node.NodeType == VisualNodeTypes.ExportVideo))
        {
            warnings.Add("Workflow ไม่มี Export Node ผลลัพธ์จะหยุดอยู่ใน Project Cache");
        }

        foreach (var node in enabledNodes)
        {
            var incomingCount = workflow.Connections.Count(connection => connection.TargetNodeId == node.NodeId);
            if (incomingCount == 0 && node.NodeType is not VisualNodeTypes.MediaInput and not VisualNodeTypes.FolderInput)
            {
                warnings.Add($"Node ‘{node.DisplayName}’ ไม่มี Input Connection");
            }
        }

        return new WorkflowValidationResult
        {
            Errors = errors,
            Warnings = warnings,
            TopologicalOrder = errors.Count == 0 ? order : []
        };
    }
}
