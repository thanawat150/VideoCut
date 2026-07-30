using System.Text;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;

namespace AutoCutStudio.Infrastructure;

public sealed class EnhancementJobFactory
{
    private readonly JobRepository _jobs;

    public EnhancementJobFactory(JobRepository jobs) => _jobs = jobs;

    public async Task<JobDocument> CreateAsync(
        ProjectDocument project,
        MediaAsset media,
        IReadOnlyList<TimelineSegment> segments,
        EnhancementPlan plan,
        CancellationToken cancellationToken = default)
    {
        PathSecurity.ValidateMp4Source(media.SourcePath);
        PathSecurity.ValidateTimelineSegments(segments, media.Metadata.DurationSeconds);
        var jobId = Guid.NewGuid();
        var jobDirectory = PathSecurity.EnsureUnderRoot(
            Path.Combine(project.RootPath, "jobs", jobId.ToString("N")), project.RootPath);
        Directory.CreateDirectory(jobDirectory);

        string? stagedMusic = null;
        if (!string.IsNullOrWhiteSpace(plan.MusicPath))
        {
            if (!File.Exists(plan.MusicPath))
                throw new FileNotFoundException("ไม่พบไฟล์เพลงที่เลือก", plan.MusicPath);
            var extension = Path.GetExtension(plan.MusicPath).ToLowerInvariant();
            if (extension is not ".mp3" and not ".wav" and not ".m4a" and not ".aac" and not ".flac" and not ".ogg")
                throw new InvalidDataException("รองรับเพลง MP3, WAV, M4A, AAC, FLAC และ OGG เท่านั้น");
            stagedMusic = Path.Combine(jobDirectory, "music" + extension);
            File.Copy(plan.MusicPath, stagedMusic, false);
        }

        var output = VersionedPathService.GetNextAvailablePath(
            Path.Combine(project.RootPath, "exports", "enhanced"),
            Path.GetFileNameWithoutExtension(media.SourcePath) + "_enhanced",
            ".mp4");
        var recipe = new RenderRecipe
        {
            PresetId = "enhancement",
            AudioEnhancementPreset = plan.AudioPreset,
            ColorPreset = plan.ColorPreset,
            Stabilize = plan.Stabilize,
            MusicPath = stagedMusic,
            EnableMusicDucking = plan.EnableMusicDucking && stagedMusic is not null,
            MusicVolume = Math.Clamp(plan.MusicVolume, 0, 1),
            VideoBitrateKbps = 9000,
            AudioBitrateKbps = 192
        };
        var job = new JobDocument
        {
            JobId = jobId,
            ProjectId = project.ProjectId,
            ProjectRoot = project.RootPath,
            JobType = JobTypes.EnhancedExport,
            Status = JobStatuses.Queued,
            Priority = 20,
            InputPath = media.SourcePath,
            OutputPath = PathSecurity.EnsureUnderRoot(output, project.RootPath),
            Segments = segments.Select(segment => new TimelineSegment
            {
                Id = segment.Id,
                StartSeconds = segment.StartSeconds,
                EndSeconds = segment.EndSeconds
            }).ToList(),
            ExpectedInputHasAudio = media.Metadata.HasAudio,
            ExpectedDurationSeconds = segments.Sum(segment => segment.DurationSeconds),
            RenderRecipe = recipe
        };

        await _jobs.WriteJobAsync(job, cancellationToken);
        await _jobs.WriteProgressAsync(job, new JobProgress
        {
            JobId = job.JobId,
            Status = JobStatuses.Queued,
            ActiveAgentId = AgentIds.Producer,
            Message = "เพิ่มงาน Enhancement เข้าคิว"
        }, cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(jobDirectory, "control.json"),
            System.Text.Json.JsonSerializer.Serialize(new JobControl(), JsonDefaults.Options),
            new UTF8Encoding(false), cancellationToken);
        foreach (var name in new[] { "run.log", "error.log", "agent-events.jsonl" })
            await File.WriteAllTextAsync(Path.Combine(jobDirectory, name), string.Empty, new UTF8Encoding(false), cancellationToken);
        return job;
    }
}
