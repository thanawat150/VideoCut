using System.Text;
using System.Text.Json;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;

namespace AutoCutStudio.Infrastructure;

public sealed class MultiClipJobFactory
{
    private readonly JobRepository _jobs;

    public MultiClipJobFactory(JobRepository jobs)
    {
        _jobs = jobs;
    }

    public async Task<JobDocument> CreateAsync(
        ProjectDocument project,
        IReadOnlyList<MediaAsset> media,
        SocialExportPreset preset,
        string transition = "cut",
        double transitionDurationSeconds = 0.35,
        bool normalizeAudio = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(preset);
        if (media.Count < 2)
        {
            throw new InvalidOperationException("การรวมคลิปต้องเลือกอย่างน้อย 2 คลิป");
        }

        foreach (var asset in media)
        {
            PathSecurity.ValidateMp4Source(asset.SourcePath);
            if (!asset.Metadata.HasVideo || asset.Metadata.DurationSeconds <= 0)
            {
                throw new InvalidDataException($"ไฟล์ไม่มี Video Stream ที่ใช้งานได้: {asset.SourcePath}");
            }
        }

        var normalizedTransition = transition.Trim().ToLowerInvariant() switch
        {
            "crossfade" or "fade" => "crossfade",
            _ => "cut"
        };
        var transitionSeconds = normalizedTransition == "crossfade"
            ? Math.Clamp(transitionDurationSeconds, 0.1, 1.5)
            : 0;
        var expectedDuration = media.Sum(item => item.Metadata.DurationSeconds) -
                               transitionSeconds * Math.Max(0, media.Count - 1);
        var jobId = Guid.NewGuid();
        var jobDirectory = PathSecurity.EnsureUnderRoot(
            Path.Combine(project.RootPath, "jobs", jobId.ToString("N")), project.RootPath);
        Directory.CreateDirectory(jobDirectory);

        var output = VersionedPathService.GetNextAvailablePath(
            Path.Combine(project.RootPath, "exports", "combined"),
            $"{SanitizeName(project.DisplayName)}_{preset.Id}_combined",
            ".mp4");
        var recipe = new MultiClipRenderRecipe
        {
            Inputs = media.Select((asset, index) => new MultiClipInput
            {
                MediaAssetId = asset.Id,
                SourcePath = asset.SourcePath,
                Order = index,
                TrimStartSeconds = 0,
                TrimEndSeconds = asset.Metadata.DurationSeconds
            }).ToList(),
            Transition = normalizedTransition,
            TransitionDurationSeconds = transitionSeconds,
            NormalizeAudio = normalizeAudio
        };
        var renderRecipe = new RenderRecipe
        {
            PresetId = preset.Id,
            OutputWidth = preset.Width,
            OutputHeight = preset.Height,
            OutputFrameRate = preset.FrameRate,
            AspectStrategy = preset.AspectStrategy,
            VideoBitrateKbps = preset.VideoBitrateKbps,
            AudioBitrateKbps = preset.AudioBitrateKbps,
            CaptionSafeZone = preset.CaptionSafeZone
        };
        var job = new JobDocument
        {
            JobId = jobId,
            ProjectId = project.ProjectId,
            ProjectRoot = project.RootPath,
            JobType = JobTypes.MultiClipExport,
            Status = JobStatuses.Queued,
            Priority = 15,
            InputPath = media[0].SourcePath,
            OutputPath = PathSecurity.EnsureUnderRoot(output, project.RootPath),
            ExpectedInputHasAudio = media.Any(item => item.Metadata.HasAudio),
            ExpectedDurationSeconds = Math.Max(0.1, expectedDuration),
            MultiClipRecipe = recipe,
            RenderRecipe = renderRecipe
        };

        await _jobs.WriteJobAsync(job, cancellationToken);
        await _jobs.WriteProgressAsync(job, new JobProgress
        {
            JobId = job.JobId,
            Status = JobStatuses.Queued,
            Progress = 0,
            ActiveAgentId = AgentIds.Producer,
            Message = $"เพิ่มงานรวม {media.Count} คลิปเข้าคิว"
        }, cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(jobDirectory, "control.json"),
            JsonSerializer.Serialize(new JobControl(), JsonDefaults.Options),
            new UTF8Encoding(false),
            cancellationToken);
        foreach (var name in new[] { "run.log", "error.log", "agent-events.jsonl" })
        {
            await File.WriteAllTextAsync(
                Path.Combine(jobDirectory, name),
                string.Empty,
                new UTF8Encoding(false),
                cancellationToken);
        }

        return job;
    }

    private static string SanitizeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(result) ? "AutoCut" : result;
    }
}
