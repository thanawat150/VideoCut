using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AutoCutStudio.Core.Models;

namespace AutoCutStudio.App;

public partial class AutomationStudioWindow
{
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        var card = FindNodeCard(e.OriginalSource as DependencyObject);
        if (card?.Tag is not Guid nodeId)
        {
            return;
        }

        var node = _workflow.Nodes.FirstOrDefault(item => item.NodeId == nodeId);
        if (node is null)
        {
            return;
        }

        // Select without rebuilding the canvas. Rebuilding here would replace the element
        // that owns mouse capture and make dragging stop on the first pixel.
        _selectedNode = node;
        UpdateSelectedNodeEditor();
        RefreshNodeSelectionBorders();
        _draggingControl = card;
        _dragStart = e.GetPosition(WorkflowCanvas);
        _dragNodeStart = new Point(node.X, node.Y);
        card.CaptureMouse();
        e.Handled = true;
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (_draggingControl is null ||
            _selectedNode is null ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(WorkflowCanvas);
        _selectedNode.X = Math.Clamp(
            _dragNodeStart.X + current.X - _dragStart.X,
            10,
            WorkflowCanvas.Width - 220);
        _selectedNode.Y = Math.Clamp(
            _dragNodeStart.Y + current.Y - _dragStart.Y,
            10,
            WorkflowCanvas.Height - 120);
        Canvas.SetLeft(_draggingControl, _selectedNode.X);
        Canvas.SetTop(_draggingControl, _selectedNode.Y);
        UpdateConnectionLines();
        e.Handled = true;
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        if (_draggingControl is null)
        {
            return;
        }

        _draggingControl.ReleaseMouseCapture();
        _draggingControl = null;
        _workflow.ModifiedAt = DateTimeOffset.UtcNow;
        RefreshNodeSelectionBorders();
        UpdateFlowInfo();
        e.Handled = true;
    }

    private Border? FindNodeCard(DependencyObject? current)
    {
        while (current is not null && current != this)
        {
            if (current is Border border && border.Tag is Guid)
            {
                return border;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void RefreshNodeSelectionBorders()
    {
        foreach (var item in _nodeControls)
        {
            var nodeId = item.Key;
            var card = item.Value;
            var status = _runStates.GetValueOrDefault(nodeId)?.Status;
            var isSelected = _selectedNode?.NodeId == nodeId;
            var isConnectionSource = _connectionSource == nodeId;
            card.BorderBrush = isConnectionSource
                ? Brushes.Gold
                : isSelected
                    ? new SolidColorBrush(Color.FromRgb(70, 214, 190))
                    : StatusBrush(status);
            card.BorderThickness = new Thickness(isSelected || isConnectionSource ? 2.5 : 1.5);
        }
    }
}
