using System.Text;
using System.Text.Json;
using AutoCutStudio.Core.Interfaces;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;

namespace AutoCutStudio.Infrastructure;

public sealed class JobRepository : IJobRepository
{
    public async Task<JobDocument> CreateTimelineExportJobAsync(
        ProjectDocument project,
        MediaAsset media,
        IReadOnlyList<TimelineSegment> segments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(segments);
        PathSecurity.ValidateMp4Source(media.SourcePath);
        PathSecurity.ValidateTimelineSegments(segments, media.Metadata.DurationSeconds);

        var outputPath = VersionedPathService.GetNextAvailablePath(
            Path.Combine(project.RootPath, "exports"),
            Path.GetFileNameWithoutExtension(media.SourcePath) + "_cut",
            ".mp4");
        var job = BuildJob(
            project, media, segments, JobTypes.TimelineExport, outputPath,
            recipe: null, priority: 0);
        return await InitializeJobAsync(job, cancellationToken);
    }

    public async Task<JobDocument> CreateSocialClipJobAsync(
        ProjectDocument project,
        MediaAsset media,
        HighlightCandidate candidate,
        SocialExportPreset preset,
        string assContent,
        string hookText,
        string ctaText,
        bool burnCaptions,
        IReadOnlyList<BrollOverlayRecipe>? brollOverlays = null,
        int priority = 10,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(preset);
        PathSecurity.ValidateMp4Source(media.SourcePath);

        var segment = new TimelineSegment
        {
            StartSeconds = candidate.StartSeconds,
            EndSeconds = candidate.EndSeconds
        };
        PathSecurity.ValidateTimelineSegments([segment], media.Metadata.DurationSeconds);
        var outputPath = VersionedPathService.GetNextAvailablePath(
            Path.Combine(project.RootPath, "exports", "social"),
            $"{Path.GetFileNameWithoutExtension(media.SourcePath)}_{preset.Id}_highlight_{candidate.Rank:00}",
            ".mp4");
        var jobId = Guid.NewGuid();
        var jobDirectory = PathSecurity.EnsureUnderRoot(
            Path.Combine(project.RootPath, "jobs", jobId.ToString("N")),
            project.RootPath);
        Directory.CreateDirectory(jobDirectory);
        string? captionPath = null;
        if (burnCaptions)
        {
            captionPath = Path.Combine(jobDirectory, "captions.ass");
            await File.WriteAllTextAsync(captionPath, assContent, new UTF8Encoding(false), cancellationToken);
        }

        var stagedBroll = await StageBrollAsync(
            jobDirectory,
            candidate,
            brollOverlays ?? [],
            cancellationToken);
        var recipe = new RenderRecipe
        {
            PresetId = preset.Id,
            OutputWidth = preset.Width,
            OutputHeight = preset.Height,
            OutputFrameRate = preset.FrameRate,
            AspectStrategy = preset.AspectStrategy,
            VideoBitrateKbps = preset.VideoBitrateKbps,
            AudioBitrateKbps = preset.AudioBitrateKbps,
            CaptionAssPath = captionPath,
            BurnCaptions = burnCaptions,
            HookText = hookText,
            CtaText = ctaText,
            CaptionSafeZone = preset.CaptionSafeZone,
            BrollOverlays = stagedBroll
        };
        var job = BuildJob(
            project, media, [segment], JobTypes.SocialClipExport,
            outputPath, recipe, priority, jobId);
        return await InitializeJobAsync(job, cancellationToken);
    }

    private static async Task<List<BrollOverlayRecipe>> StageBrollAsync(
        string jobDirectory,
        HighlightCandidate candidate,
        IReadOnlyList<BrollOverlayRecipe> overlays,
        CancellationToken cancellationToken)
    {
        var result = new List<BrollOverlayRecipe>();
        var index = 0;
        foreach (var overlay in overlays
                     .Where(item => item.Confidence >= 0.55)
                     .OrderByDescending(item => item.Confidence)
                     .ThenBy(item => item.StartSeconds)
                     .Take(12))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(overlay.AssetPath))
            {
                continue;
            }

            var extension = Path.GetExtension(overlay.AssetPath).ToLowerInvariant();
            if (extension is not ".mp4" and not ".mov" and not ".mkv" and
                not ".jpg" and not ".jpeg" and not ".png" and not ".webp")
            {
                continue;
            }

            var relativeStart = Math.Clamp(
                overlay.StartSeconds - candidate.StartSeconds,
                0,
                candidate.DurationSeconds);
            var relativeEnd = Math.Clamp(
                overlay.EndSeconds - candidate.StartSeconds,
                relativeStart + 0.1,
                candidate.DurationSeconds);
            if (relativeEnd <= relativeStart + 0.08)
            {
                continue;
            }

            var staged = Path.Combine(jobDirectory, $"broll-{index:00}{extension}");
            await using (var input = new FileStream(
                             overlay.AssetPath,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             1024 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(
                             staged,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             1024 * 1024,
                             FileOptions.Asynchronous))
            {
                await input.CopyToAsync(output, 1024 * 1024, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            result.Add(overlay with
            {
                AssetPath = staged,
                StartSeconds = relativeStart,
                EndSeconds = relativeEnd
            });
            index++;
        }

        return result;
    }

    private static JobDocument BuildJob(
        ProjectDocument project,
        MediaAsset media,
        IReadOnlyList<TimelineSegment> segments,
        string jobType,
        string outputPath,
        RenderRecipe? recipe,
        int priority,
        Guid? jobId = null) => new()
    {
        JobId = jobId ?? Guid.NewGuid(),
        ProjectId = project.ProjectId,
        ProjectRoot = project.RootPath,
        JobType = jobType,
        Status = JobStatuses.Queued,
        Priority = priority,
        InputPath = media.SourcePath,
        OutputPath = PathSecurity.EnsureUnderRoot(outputPath, project.RootPath),
        Segments = segments.Select(CloneSegment).ToList(),
        ExpectedInputHasAudio = media.Metadata.HasAudio,
        ExpectedDurationSeconds = segments.Sum(segment => segment.DurationSeconds),
        RenderRecipe = recipe,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private async Task<JobDocument> InitializeJobAsync(JobDocument job, CancellationToken cancellationToken)
    {
        var jobDirectory = GetJobDirectory(job);
        Directory.CreateDirectory(jobDirectory);
        await WriteJobAsync(job, cancellationToken);
        await WriteProgressAsync(job, new JobProgress
        {
            JobId = job.JobId,
            Status = JobStatuses.Queued,
            Progress = 0,
            ActiveAgentId = AgentIds.Producer,
            Message = "งานถูกเพิ่มเข้าคิว",
            UpdatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
        await AtomicJsonFile.WriteAsync(
            Path.Combine(jobDirectory, "control.json"), new JobControl(), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(jobDirectory, "run.log"), string.Empty, Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(jobDirectory, "error.log"), string.Empty, Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(jobDirectory, "agent-events.jsonl"), string.Empty, Encoding.UTF8, cancellationToken);
        return job;
    }

    public Task<JobDocument> ReadJobAsync(string jobFilePath, CancellationToken cancellationToken = default)
        => AtomicJsonFile.ReadAsync<JobDocument>(jobFilePath, cancellationToken);

    public Task WriteJobAsync(JobDocument job, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(GetJobDirectory(job), "job.json");
        return AtomicJsonFile.WriteAsync(path, job with { UpdatedAt = DateTimeOffset.UtcNow }, cancellationToken);
    }

    public Task WriteProgressAsync(JobDocument job, JobProgress progress, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(GetJobDirectory(job), "progress.json");
        return AtomicJsonFile.WriteAsync(path, progress with { UpdatedAt = DateTimeOffset.UtcNow }, cancellationToken);
    }

    public async Task AppendEventAsync(JobDocument job, AgentEvent agentEvent, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(GetJobDirectory(job), "agent-events.jsonl");
        var json = JsonSerializer.Serialize(agentEvent, JsonDefaults.CompactOptions);
        await AppendLineAsync(path, json, cancellationToken);
    }

    public Task AppendRunLogAsync(JobDocument job, string line, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(GetJobDirectory(job), "run.log");
        return AppendLineAsync(path, $"[{DateTimeOffset.UtcNow:O}] {line}", cancellationToken);
    }

    public Task WriteErrorAsync(JobDocument job, string content, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(GetJobDirectory(job), "error.log");
        return File.WriteAllTextAsync(path, content, Encoding.UTF8, cancellationToken);
    }

    public async Task<IReadOnlyList<JobDocument>> ListJobsAsync(ProjectDocument project, CancellationToken cancellationToken = default)
    {
        var jobsRoot = Path.Combine(project.RootPath, "jobs");
        if (!Directory.Exists(jobsRoot))
        {
            return [];
        }

        var jobs = new List<JobDocument>();
        foreach (var file in Directory.EnumerateFiles(jobsRoot, "job.json", SearchOption.AllDirectories))
        {
            try
            {
                jobs.Add(await ReadJobAsync(file, cancellationToken));
            }
            catch
            {
                // A corrupt job remains visible through its files; skip it from the runnable queue.
            }
        }
        return jobs.OrderBy(job => job.Priority).ThenBy(job => job.CreatedAt).ToList();
    }

    public string GetJobDirectory(JobDocument job)
    {
        var jobsRoot = Path.Combine(job.ProjectRoot, "jobs");
        var path = Path.Combine(jobsRoot, job.JobId.ToString("N"));
        return PathSecurity.EnsureUnderRoot(path, job.ProjectRoot);
    }

    public static string GetJobFilePath(JobDocument job)
        => Path.Combine(job.ProjectRoot, "jobs", job.JobId.ToString("N"), "job.json");

    private static TimelineSegment CloneSegment(TimelineSegment segment) => new()
    {
        Id = segment.Id,
        StartSeconds = segment.StartSeconds,
        EndSeconds = segment.EndSeconds
    };

    private static async Task AppendLineAsync(string path, string line, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = new FileStream(
            path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite,
            16 * 1024, FileOptions.Asynchronous);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }
}
