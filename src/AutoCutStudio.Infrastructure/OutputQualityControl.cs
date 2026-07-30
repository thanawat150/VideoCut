using System.Security.Cryptography;
using AutoCutStudio.Core.Interfaces;
using AutoCutStudio.Core.Models;

namespace AutoCutStudio.Infrastructure;

public sealed class OutputQualityControl : IOutputQualityControl
{
    private readonly IMediaProbe _mediaProbe;

    public OutputQualityControl(IMediaProbe mediaProbe)
    {
        _mediaProbe = mediaProbe;
    }

    public async Task<(QaReport Report, ExportManifest? Manifest)> ValidateAsync(
        JobDocument job,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var outputExists = File.Exists(job.OutputPath);

        if (!outputExists)
        {
            errors.Add("ไม่พบไฟล์ Output");
            return (new QaReport
            {
                JobId = job.JobId,
                Passed = false,
                OutputExists = false,
                ExpectedAudio = job.ExpectedInputHasAudio,
                ExpectedDurationSeconds = job.ExpectedDurationSeconds,
                Errors = errors,
                Warnings = warnings
            }, null);
        }

        MediaMetadata metadata;
        try
        {
            metadata = await _mediaProbe.ProbeAsync(job.OutputPath, cancellationToken);
        }
        catch (Exception exception)
        {
            errors.Add($"FFprobe ตรวจ Output ไม่สำเร็จ: {exception.Message}");
            return (new QaReport
            {
                JobId = job.JobId,
                Passed = false,
                OutputExists = true,
                ExpectedAudio = job.ExpectedInputHasAudio,
                ExpectedDurationSeconds = job.ExpectedDurationSeconds,
                Errors = errors,
                Warnings = warnings
            }, null);
        }

        if (!metadata.HasVideo)
        {
            errors.Add("Output ไม่มี Video Stream");
        }

        if (job.ExpectedInputHasAudio && !metadata.HasAudio)
        {
            errors.Add("Output ไม่มี Audio Stream ทั้งที่ Source มีเสียง");
        }

        var durationDifference = Math.Abs(metadata.DurationSeconds - job.ExpectedDurationSeconds);
        if (durationDifference > 0.75)
        {
            errors.Add(
                $"Duration ไม่ตรงตาม Timeline: คาด {job.ExpectedDurationSeconds:0.###} วินาที แต่ได้ {metadata.DurationSeconds:0.###} วินาที");
        }
        else if (durationDifference > 0.25)
        {
            warnings.Add($"Duration ต่างจาก Timeline {durationDifference:0.###} วินาที");
        }

        var fileInfo = new FileInfo(job.OutputPath);
        if (fileInfo.Length == 0)
        {
            errors.Add("Output มีขนาด 0 ไบต์");
        }

        var sha256 = await CalculateSha256Async(job.OutputPath, cancellationToken);
        var report = new QaReport
        {
            JobId = job.JobId,
            Passed = errors.Count == 0,
            OutputExists = true,
            HasVideo = metadata.HasVideo,
            HasAudio = metadata.HasAudio,
            ExpectedAudio = job.ExpectedInputHasAudio,
            ExpectedDurationSeconds = job.ExpectedDurationSeconds,
            ActualDurationSeconds = metadata.DurationSeconds,
            FileSizeBytes = fileInfo.Length,
            Errors = errors,
            Warnings = warnings,
            CheckedAt = DateTimeOffset.UtcNow
        };

        var manifest = errors.Count == 0
            ? new ExportManifest
            {
                JobId = job.JobId,
                ProjectId = job.ProjectId,
                InputPath = job.InputPath,
                OutputPath = job.OutputPath,
                Sha256 = sha256,
                FileSizeBytes = fileInfo.Length,
                DurationSeconds = metadata.DurationSeconds,
                HasVideo = metadata.HasVideo,
                HasAudio = metadata.HasAudio,
                CreatedAt = DateTimeOffset.UtcNow
            }
            : null;

        return (report, manifest);
    }

    private static async Task<string> CalculateSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
