using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;

namespace AutoCutStudio.Infrastructure;

public sealed class NestedSequenceRepository
{
    public async Task SaveAsync(
        ProjectDocument project,
        NestedSequenceDocument sequence,
        CancellationToken cancellationToken = default)
    {
        if (sequence.ProjectId != project.ProjectId)
            throw new InvalidDataException("Nested Sequence ไม่ได้เป็นของ Project นี้");
        if (sequence.Clips.Count == 0)
            throw new InvalidDataException("Nested Sequence ต้องมีอย่างน้อย 1 Clip");
        var directory = PathSecurity.EnsureUnderRoot(Path.Combine(project.RootPath, "sequences"), project.RootPath);
        Directory.CreateDirectory(directory);
        var path = PathSecurity.EnsureUnderRoot(Path.Combine(directory, sequence.SequenceId.ToString("N") + ".json"), project.RootPath);
        await AtomicJsonFile.WriteAsync(path, sequence with { ModifiedAt = DateTimeOffset.UtcNow }, cancellationToken);
    }

    public async Task<IReadOnlyList<NestedSequenceDocument>> ListAsync(
        ProjectDocument project,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(project.RootPath, "sequences");
        if (!Directory.Exists(directory)) return [];
        var result = new List<NestedSequenceDocument>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var item = await AtomicJsonFile.ReadAsync<NestedSequenceDocument>(path, cancellationToken);
                if (item.ProjectId == project.ProjectId) result.Add(item);
            }
            catch
            {
                // Corrupt sequence remains on disk for diagnostics but is not loaded.
            }
        }
        return result.OrderByDescending(item => item.ModifiedAt).ToList();
    }
}
