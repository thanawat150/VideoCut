using System.Text;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;

namespace AutoCutStudio.Infrastructure;

public sealed class PrivacyBlurJobFactory
{
    private readonly JobRepository _jobs;

    public PrivacyBlurJobFactory(JobRepository jobs) => _jobs = jobs;

    public async Task<JobDocument> CreateAsync(
        ProjectDocument project,
        MediaAsset media,
        FaceTrackingResult tracking,
        PrivacyBlurPlan plan,
        CancellationToken cancellationToken = default)
    {
        PathSecurity.ValidateMp4Source(media.SourcePath);
        var selected = plan.SelectedTracks
            .Where(track => track.Keyframes.Count > 0)
            .Take(20)
            .ToList();
        if (selected.Count == 0)
            throw new InvalidDataException("เลือก Face Track อย่างน้อย 1 รายการ");
        if (tracking.SourceWidth <= 0 || tracking.SourceHeight <= 0)
            throw new InvalidDataException("Face Tracking ไม่มีขนาด Source ที่ถูกต้อง");

        var jobId = Guid.NewGuid();
        var output = VersionedPathService.GetNextAvailablePath(
            Path.Combine(project.RootPath, "exports", "privacy"),
            Path.GetFileNameWithoutExtension(media.SourcePath) + "_privacy_blur",
            ".mp4");
        var job = new JobDocument
        {
            JobId = jobId,
            ProjectId = project.ProjectId,
            ProjectRoot = project.RootPath,
            JobType = AdvancedJobTypes.PrivacyBlurExport,
            Status = JobStatuses.Queued,
            Priority = 15,
            InputPath = media.SourcePath,
            OutputPath = PathSecurity.EnsureUnderRoot(output, project.RootPath),
            Segments =
            [
                new TimelineSegment
                {
                    StartSeconds = 0,
                    EndSeconds = media.Metadata.DurationSeconds
                }
            ],
            ExpectedInputHasAudio = media.Metadata.HasAudio,
            ExpectedDurationSeconds = media.Metadata.DurationSeconds,
            PrivacyBlurRecipe = new PrivacyBlurRecipe
            {
                Tracks = selected,
                BlurStrength = Math.Clamp(plan.BlurStrength, 5, 40),
                PaddingRatio = Math.Clamp(plan.BoxPaddingRatio, 0, 0.6),
                SampleIntervalSeconds = tracking.SampleIntervalSeconds
            }
        };
        await InitializeAsync(job, cancellationToken);
        return job;
    }

    private async Task InitializeAsync(JobDocument job, CancellationToken cancellationToken)
    {
        var directory = _jobs.GetJobDirectory(job);
        Directory.CreateDirectory(directory);
        await _jobs.WriteJobAsync(job, cancellationToken);
        await _jobs.WriteProgressAsync(job, new JobProgress
        {
            JobId = job.JobId,
            Status = JobStatuses.Queued,
            ActiveAgentId = AgentIds.Producer,
            Message = "เพิ่ม Privacy Blur Job เข้าคิว"
        }, cancellationToken);
        await AtomicJsonFile.WriteAsync(Path.Combine(directory, "control.json"), new JobControl(), cancellationToken);
        foreach (var name in new[] { "run.log", "error.log", "agent-events.jsonl" })
            await File.WriteAllTextAsync(Path.Combine(directory, name), string.Empty, new UTF8Encoding(false), cancellationToken);
    }
}
