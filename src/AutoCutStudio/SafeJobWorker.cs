using System.Text.Json;

namespace AutoCutStudio;

/// <summary>
/// Processes persisted jobs without keeping job.json open while replacing it.
/// Progress is sampled and written by one loop, preventing concurrent writes to the same .tmp file.
/// </summary>
public static class SafeJobWorker
{
    public static async Task RunAsync(bool runOnce, CancellationToken cancellationToken = default)
    {
        var jobsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoCutStudio",
            "Jobs");
        Directory.CreateDirectory(jobsRoot);

        var exporter = new FfmpegExportService();
        while (!cancellationToken.IsCancellationRequested)
        {
            var claimed = await ClaimNextAsync(jobsRoot, cancellationToken);
            if (claimed is null)
            {
                if (runOnce) return;
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                continue;
            }

            await ProcessAsync(claimed, exporter, cancellationToken);
            if (runOnce) return;
        }
    }

    private static async Task<ClaimedJob?> ClaimNextAsync(string jobsRoot, CancellationToken cancellationToken)
    {
        foreach (var jobPath in Directory
                     .EnumerateFiles(jobsRoot, "job.json", SearchOption.AllDirectories)
                     .OrderBy(File.GetCreationTimeUtc))
        {
            JobRecord? job;
            await using (var stream = new FileStream(
                             jobPath,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.ReadWrite | FileShare.Delete,
                             64 * 1024,
                             useAsync: true))
            {
                job = await JsonSerializer.DeserializeAsync<JobRecord>(
                    stream,
                    JsonFiles.Options,
                    cancellationToken);
            }

            if (job?.Status is not (JobStatus.Waiting or JobStatus.Resumable)) continue;

            var directory = Path.GetDirectoryName(jobPath)
                ?? throw new InvalidDataException("Job path ไม่มีโฟลเดอร์แม่");
            job.Status = JobStatus.Preparing;
            await SaveAsync(directory, job, cancellationToken);
            return new ClaimedJob(job, directory);
        }

        return null;
    }

    private static async Task ProcessAsync(
        ClaimedJob claimed,
        FfmpegExportService exporter,
        CancellationToken cancellationToken)
    {
        var job = claimed.Job;
        if (job.Export is null)
        {
            job.Status = JobStatus.Failed;
            job.ErrorMessage = "Job ไม่มีข้อมูล Export";
            await SaveAsync(claimed.Directory, job, cancellationToken);
            return;
        }

        try
        {
            job.Status = JobStatus.Exporting;
            await SaveAsync(claimed.Directory, job, cancellationToken);
            await AppendLogAsync(
                claimed.Directory,
                "run.log",
                $"Export started: {job.Export.InputPath}",
                cancellationToken);

            var progress = new LatestProgress();
            var exportTask = exporter.ExportAsync(job.Export, progress, cancellationToken);
            var lastSaved = -1;

            while (!exportTask.IsCompleted)
            {
                var percent = progress.Value;
                if (percent != lastSaved)
                {
                    job.ProgressPercent = percent;
                    await SaveAsync(claimed.Directory, job, cancellationToken);
                    lastSaved = percent;
                }

                await Task.WhenAny(exportTask, Task.Delay(250, cancellationToken));
            }

            await exportTask;
            job.ProgressPercent = 100;
            job.Status = JobStatus.Completed;
            job.ErrorMessage = null;
            await SaveAsync(claimed.Directory, job, cancellationToken);
            await AppendLogAsync(
                claimed.Directory,
                "run.log",
                $"Export completed: {job.Export.OutputPath}",
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            job.Status = JobStatus.Cancelled;
            job.ErrorMessage = "งานถูกยกเลิก";
            await SaveAsync(claimed.Directory, job, CancellationToken.None);
        }
        catch (Exception ex)
        {
            job.Status = JobStatus.Failed;
            job.ErrorMessage = ex.Message;
            await SaveAsync(claimed.Directory, job, CancellationToken.None);
            await AppendLogAsync(
                claimed.Directory,
                "error.log",
                ex.ToString(),
                CancellationToken.None);
        }
    }

    private static async Task SaveAsync(
        string directory,
        JobRecord job,
        CancellationToken cancellationToken)
    {
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await JsonFiles.WriteAtomicAsync(
            Path.Combine(directory, "job.json"),
            job,
            cancellationToken);
        await JsonFiles.WriteAtomicAsync(
            Path.Combine(directory, "progress.json"),
            new
            {
                status = job.Status,
                percent = job.ProgressPercent,
                updatedAt = job.UpdatedAt,
                error = job.ErrorMessage
            },
            cancellationToken);
    }

    private static Task AppendLogAsync(
        string directory,
        string fileName,
        string message,
        CancellationToken cancellationToken) =>
        File.AppendAllTextAsync(
            Path.Combine(directory, fileName),
            $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}",
            cancellationToken);

    private sealed record ClaimedJob(JobRecord Job, string Directory);

    private sealed class LatestProgress : IProgress<int>
    {
        private int _value;
        public int Value => Volatile.Read(ref _value);
        public void Report(int value) => Interlocked.Exchange(ref _value, Math.Clamp(value, 0, 100));
    }
}
