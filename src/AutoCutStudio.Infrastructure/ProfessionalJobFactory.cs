using System.Text;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;

namespace AutoCutStudio.Infrastructure;

public sealed class ProfessionalJobFactory
{
    private readonly JobRepository _jobs;

    public ProfessionalJobFactory(JobRepository jobs) => _jobs = jobs;

    public Task<JobDocument> CreateMulticamAsync(
        ProjectDocument project,
        MulticamRenderRecipe recipe,
        CancellationToken cancellationToken = default)
    {
        if (recipe.Angles.Count < 2) throw new InvalidDataException("Multicam ต้องมีอย่างน้อย 2 กล้อง");
        if (recipe.Switches.Count == 0) throw new InvalidDataException("Multicam ต้องมี Switch อย่างน้อย 1 ช่วง");
        var angleIds = recipe.Angles.Select(item => item.AngleId).ToHashSet();
        foreach (var angle in recipe.Angles)
            PathSecurity.ValidateMp4Source(angle.SourcePath);
        foreach (var item in recipe.Switches)
        {
            if (!angleIds.Contains(item.AngleId)) throw new InvalidDataException("Switch อ้างถึงกล้องที่ไม่มีในแผน");
            if (item.DurationSeconds <= 0) throw new InvalidDataException("ช่วง Multicam ต้องมีความยาวมากกว่า 0");
        }
        var output = VersionedPathService.GetNextAvailablePath(
            Path.Combine(project.RootPath, "exports", "multicam"), "multicam_edit", ".mp4");
        var job = Build(
            project,
            ProfessionalJobTypes.MulticamExport,
            recipe.Angles[0].SourcePath,
            output,
            recipe.Switches.Sum(item => item.DurationSeconds),
            recipe.Angles.All(item => item.HasAudio)) with { MulticamRecipe = recipe };
        return InitializeAsync(job, cancellationToken);
    }

    public Task<JobDocument> CreateKeyframeAsync(
        ProjectDocument project,
        MediaAsset media,
        KeyframeRenderRecipe recipe,
        CancellationToken cancellationToken = default)
    {
        PathSecurity.ValidateMp4Source(media.SourcePath);
        var keyframes = recipe.Keyframes.OrderBy(item => item.TimeSeconds).ToList();
        if (keyframes.Count < 2) throw new InvalidDataException("Keyframe Export ต้องมีอย่างน้อย 2 Keyframe");
        if (keyframes[0].TimeSeconds < 0 || keyframes[^1].TimeSeconds > media.Metadata.DurationSeconds + 0.01)
            throw new InvalidDataException("เวลา Keyframe อยู่นอกช่วงวิดีโอ");
        for (var index = 0; index < keyframes.Count; index++)
        {
            var item = keyframes[index];
            if (item.Zoom is < 1 or > 4 || item.FocusX is < 0 or > 1 || item.FocusY is < 0 or > 1 || item.AudioGain is < 0 or > 4)
                throw new InvalidDataException("ค่าของ Keyframe อยู่นอกขอบเขตที่อนุญาต");
            if (index > 0 && item.TimeSeconds <= keyframes[index - 1].TimeSeconds)
                throw new InvalidDataException("เวลา Keyframe ต้องเรียงจากน้อยไปมากและไม่ซ้ำ");
        }
        recipe = recipe with { Keyframes = keyframes };
        var output = VersionedPathService.GetNextAvailablePath(
            Path.Combine(project.RootPath, "exports", "keyframes"),
            Path.GetFileNameWithoutExtension(media.SourcePath) + "_keyframes", ".mp4");
        var job = Build(
            project,
            ProfessionalJobTypes.KeyframeExport,
            media.SourcePath,
            output,
            keyframes[^1].TimeSeconds - keyframes[0].TimeSeconds,
            media.Metadata.HasAudio) with { KeyframeRecipe = recipe };
        return InitializeAsync(job, cancellationToken);
    }

    public Task<JobDocument> CreateNestedSequenceAsync(
        ProjectDocument project,
        NestedSequenceRenderRecipe recipe,
        CancellationToken cancellationToken = default)
    {
        if (recipe.Clips.Count == 0) throw new InvalidDataException("Nested Sequence ไม่มี Clip");
        foreach (var clip in recipe.Clips)
        {
            PathSecurity.ValidateMp4Source(clip.SourcePath);
            if (clip.DurationSeconds <= 0) throw new InvalidDataException("Nested Clip ต้องมีความยาวมากกว่า 0");
        }
        var output = VersionedPathService.GetNextAvailablePath(
            Path.Combine(project.RootPath, "exports", "nested"),
            VersionedPathService.SanitizeFileName(recipe.Name), ".mp4");
        var job = Build(
            project,
            ProfessionalJobTypes.NestedSequenceExport,
            recipe.Clips[0].SourcePath,
            output,
            recipe.Clips.Sum(item => item.DurationSeconds),
            recipe.Clips.All(item => item.HasAudio)) with { NestedSequenceRecipe = recipe };
        return InitializeAsync(job, cancellationToken);
    }

    private static JobDocument Build(
        ProjectDocument project,
        string jobType,
        string input,
        string output,
        double duration,
        bool hasAudio) => new()
    {
        ProjectId = project.ProjectId,
        ProjectRoot = project.RootPath,
        JobType = jobType,
        Status = JobStatuses.Queued,
        Priority = 40,
        InputPath = input,
        OutputPath = PathSecurity.EnsureUnderRoot(output, project.RootPath),
        Segments = [new TimelineSegment { StartSeconds = 0, EndSeconds = duration }],
        ExpectedInputHasAudio = hasAudio,
        ExpectedDurationSeconds = duration
    };

    private async Task<JobDocument> InitializeAsync(JobDocument job, CancellationToken cancellationToken)
    {
        var directory = _jobs.GetJobDirectory(job);
        Directory.CreateDirectory(directory);
        await _jobs.WriteJobAsync(job, cancellationToken);
        await _jobs.WriteProgressAsync(job, new JobProgress
        {
            JobId = job.JobId,
            Status = JobStatuses.Queued,
            Progress = 0,
            ActiveAgentId = AgentIds.Producer,
            Message = $"เพิ่ม {job.JobType} เข้าคิว"
        }, cancellationToken);
        await AtomicJsonFile.WriteAsync(Path.Combine(directory, "control.json"), new JobControl(), cancellationToken);
        foreach (var name in new[] { "run.log", "error.log", "agent-events.jsonl" })
            await File.WriteAllTextAsync(Path.Combine(directory, name), string.Empty, new UTF8Encoding(false), cancellationToken);
        return job;
    }
}
