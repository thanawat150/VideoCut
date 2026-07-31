using System.Text;
using System.Text.Json;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;

namespace AutoCutStudio.Infrastructure;

public sealed class VisualWorkflowRepository
{
    public string GetWorkflowDirectory(string projectRoot) =>
        PathSecurity.EnsureUnderRoot(Path.Combine(projectRoot, "workflows"), projectRoot);

    public string GetRunDirectory(string projectRoot) =>
        PathSecurity.EnsureUnderRoot(Path.Combine(projectRoot, "workflow-runs"), projectRoot);

    public async Task SaveAsync(
        string projectRoot,
        VisualWorkflowDocument workflow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var directory = GetWorkflowDirectory(projectRoot);
        Directory.CreateDirectory(directory);
        workflow.ModifiedAt = DateTimeOffset.UtcNow;
        var path = GetWorkflowPath(projectRoot, workflow.WorkflowId);
        await WriteAtomicAsync(path, workflow, cancellationToken);
    }

    public async Task<VisualWorkflowDocument?> OpenAsync(
        string projectRoot,
        Guid workflowId,
        CancellationToken cancellationToken = default)
    {
        var path = GetWorkflowPath(projectRoot, workflowId);
        return File.Exists(path)
            ? await ReadAsync<VisualWorkflowDocument>(path, cancellationToken)
            : null;
    }

    public async Task<IReadOnlyList<VisualWorkflowDocument>> ListAsync(
        string projectRoot,
        CancellationToken cancellationToken = default)
    {
        var directory = GetWorkflowDirectory(projectRoot);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var workflows = new List<VisualWorkflowDocument>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.workflow.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var workflow = await ReadAsync<VisualWorkflowDocument>(path, cancellationToken);
                workflows.Add(workflow);
            }
            catch (JsonException)
            {
                // Leave corrupt workflow files on disk for manual recovery, but do not load them.
            }
        }

        return workflows.OrderByDescending(item => item.ModifiedAt).ToList();
    }

    public async Task DeleteAsync(
        string projectRoot,
        Guid workflowId,
        CancellationToken cancellationToken = default)
    {
        var path = GetWorkflowPath(projectRoot, workflowId);
        if (!File.Exists(path))
        {
            return;
        }

        await Task.Run(() => File.Delete(path), cancellationToken);
    }

    public async Task SaveRunAsync(
        string projectRoot,
        WorkflowRunDocument run,
        CancellationToken cancellationToken = default)
    {
        var directory = GetRunDirectory(projectRoot);
        Directory.CreateDirectory(directory);
        run.UpdatedAt = DateTimeOffset.UtcNow;
        var path = PathSecurity.EnsureUnderRoot(
            Path.Combine(directory, $"{run.RunId:N}.run.json"), projectRoot);
        await WriteAtomicAsync(path, run, cancellationToken);
    }

    public async Task<WorkflowRunDocument?> OpenRunAsync(
        string projectRoot,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var path = PathSecurity.EnsureUnderRoot(
            Path.Combine(GetRunDirectory(projectRoot), $"{runId:N}.run.json"), projectRoot);
        return File.Exists(path)
            ? await ReadAsync<WorkflowRunDocument>(path, cancellationToken)
            : null;
    }

    public string GetWorkflowPath(string projectRoot, Guid workflowId) =>
        PathSecurity.EnsureUnderRoot(
            Path.Combine(GetWorkflowDirectory(projectRoot), $"{workflowId:N}.workflow.json"),
            projectRoot);

    private static async Task<T> ReadAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonDefaults.Options, cancellationToken)
               ?? throw new InvalidDataException($"อ่าน Workflow ไม่สำเร็จ: {path}");
    }

    private static async Task WriteAtomicAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(
            temporary,
            JsonSerializer.Serialize(value, JsonDefaults.Options),
            new UTF8Encoding(false),
            cancellationToken);
        File.Move(temporary, path, true);
    }
}
