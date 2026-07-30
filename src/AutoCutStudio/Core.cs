using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AutoCutStudio;

public sealed class ProjectDocument
{
    public Guid ProjectId { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Untitled";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string ApplicationVersion { get; set; } = "0.1.0";
    public List<MediaItem> SourceFiles { get; init; } = [];
    public List<TimelineTrack> Tracks { get; init; } = [new TimelineTrack { Name = "Video 1" }];
    public ExportSettings ExportSettings { get; set; } = new();
    public List<ProcessingHistoryItem> ProcessingHistory { get; init; } = [];
}

public sealed class MediaItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string OriginalPath { get; init; }
    public required string ProjectPath { get; init; }
    public required string FileName { get; init; }
    public long FileSize { get; init; }
    public string? VideoCodec { get; init; }
    public string? AudioCodec { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public double FrameRate { get; init; }
    public TimeSpan Duration { get; init; }
    public bool HasAudio { get; init; }
    public DateTimeOffset ImportedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class TimelineTrack
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Video 1";
    public bool IsLocked { get; set; }
    public bool IsMuted { get; set; }
    public List<TimelineClip> Clips { get; init; } = [];
}

public sealed class TimelineClip
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid MediaId { get; init; }
    public string DisplayName { get; set; } = "Clip";
    public TimeSpan SourceIn { get; set; }
    public TimeSpan SourceOut { get; set; }
    public TimeSpan TimelineStart { get; set; }
    [JsonIgnore] public TimeSpan Duration => SourceOut - SourceIn;
}

public sealed class ExportSettings
{
    public string Preset { get; set; } = "YouTube 1080p";
    public string Codec { get; set; } = "libx264";
    public int Crf { get; set; } = 20;
    public string AudioCodec { get; set; } = "aac";
    public int AudioBitrateKbps { get; set; } = 192;
}

public sealed class ProcessingHistoryItem
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public required string Action { get; init; }
    public required string Result { get; init; }
}

public sealed class MediaProbeResult
{
    public string? VideoCodec { get; init; }
    public string? AudioCodec { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public double FrameRate { get; init; }
    public TimeSpan Duration { get; init; }
    public long Bitrate { get; init; }
    public int AudioChannels { get; init; }
    public int AudioSampleRate { get; init; }
    public string? PixelFormat { get; init; }
    public int Rotation { get; init; }
    public bool HasAudio => !string.IsNullOrWhiteSpace(AudioCodec);
}

public sealed class ExportRequest
{
    public Guid JobId { get; init; } = Guid.NewGuid();
    public required string InputPath { get; init; }
    public required string OutputPath { get; init; }
    public TimeSpan Start { get; init; }
    public TimeSpan Duration { get; init; }
    public ExportSettings Settings { get; init; } = new();
}

public sealed class JobRecord
{
    public Guid JobId { get; init; } = Guid.NewGuid();
    public string Type { get; init; } = "export";
    public JobStatus Status { get; set; } = JobStatus.Waiting;
    public int ProgressPercent { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ExportRequest? Export { get; init; }
    public string? ErrorMessage { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum JobStatus
{
    Draft, Waiting, Preparing, Analysing, Processing, Exporting,
    Completed, CompletedWithWarnings, Failed, Cancelled, Paused, Resumable
}

internal static class JsonFiles
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    internal static async Task WriteAtomicAsync<T>(string path, T value, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Invalid file path."));
        var temporaryPath = path + ".tmp";
        await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, true))
        {
            await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        File.Move(temporaryPath, path, true);
    }
}

internal static class ToolLocator
{
    internal static string Resolve(string executableName)
    {
        var fileName = executableName + ".exe";
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(AppContext.BaseDirectory, "tools", fileName)
        };
        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists) ?? executableName;
    }
}

public class FfprobeService
{
    private readonly string _ffprobePath;
    public FfprobeService(string? ffprobePath = null) => _ffprobePath = ffprobePath ?? ToolLocator.Resolve("ffprobe");

    public virtual async Task<MediaProbeResult> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath)) throw new FileNotFoundException("ไม่พบไฟล์สื่อที่ต้องการตรวจสอบ", filePath);

        var startInfo = new ProcessStartInfo
        {
            FileName = _ffprobePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[] { "-v", "error", "-print_format", "json", "-show_format", "-show_streams", filePath })
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        try { process.Start(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException("เปิด FFprobe ไม่ได้ โปรดวาง ffprobe.exe ในโฟลเดอร์ tools หรือเพิ่ม FFmpeg ลง PATH", ex);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0) throw new InvalidDataException($"FFprobe ไม่สำเร็จ (Exit {process.ExitCode}): {stderr.Trim()}");

        using var document = JsonDocument.Parse(stdout);
        var streams = document.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var video = streams.FirstOrDefault(element => GetString(element, "codec_type") == "video");
        var audio = streams.FirstOrDefault(element => GetString(element, "codec_type") == "audio");
        var format = document.RootElement.GetProperty("format");
        var rotation = 0;
        if (video.ValueKind == JsonValueKind.Object && video.TryGetProperty("tags", out var tags) && tags.TryGetProperty("rotate", out var rotate))
            int.TryParse(rotate.GetString(), CultureInfo.InvariantCulture, out rotation);

        return new MediaProbeResult
        {
            VideoCodec = GetString(video, "codec_name"), AudioCodec = GetString(audio, "codec_name"),
            Width = GetInt(video, "width"), Height = GetInt(video, "height"),
            FrameRate = ParseFraction(GetString(video, "avg_frame_rate")),
            Duration = TimeSpan.FromSeconds(Math.Max(0, ParseDouble(GetString(format, "duration")))),
            Bitrate = (long)ParseDouble(GetString(format, "bit_rate")),
            AudioChannels = GetInt(audio, "channels"),
            AudioSampleRate = (int)ParseDouble(GetString(audio, "sample_rate")),
            PixelFormat = GetString(video, "pix_fmt"), Rotation = rotation
        };
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value.ToString() : null;
    private static int GetInt(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : 0;
    private static double ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0;
    private static double ParseFraction(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        var parts = value.Split('/');
        if (parts.Length != 2) return ParseDouble(value);
        var denominator = ParseDouble(parts[1]);
        return denominator == 0 ? 0 : ParseDouble(parts[0]) / denominator;
    }
}

public sealed class ProjectService(FfprobeService probe)
{
    private static readonly string[] ProjectFolders =
    ["source", "audio", "images", "proxy", "transcript", "subtitles", "assets", "cache", "drafts", "exports", "thumbnails", "reports", "versions"];

    public async Task<(ProjectDocument Project, string ProjectDirectory)> CreateAsync(string parentDirectory, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var safeName = string.Concat(name.Trim().Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var projectDirectory = UniqueDirectory(Path.Combine(Path.GetFullPath(parentDirectory), safeName));
        Directory.CreateDirectory(projectDirectory);
        foreach (var folder in ProjectFolders) Directory.CreateDirectory(Path.Combine(projectDirectory, folder));
        var project = new ProjectDocument { Name = name.Trim() };
        await SaveAsync(project, projectDirectory, cancellationToken);
        return (project, projectDirectory);
    }

    public async Task<ProjectDocument> LoadAsync(string projectFile, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(projectFile)) throw new FileNotFoundException("ไม่พบ project.json", projectFile);
        await using var stream = new FileStream(projectFile, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
        return await JsonSerializer.DeserializeAsync<ProjectDocument>(stream, JsonFiles.Options, cancellationToken)
            ?? throw new InvalidDataException("project.json ไม่สมบูรณ์หรืออ่านไม่ได้");
    }

    public async Task SaveAsync(ProjectDocument project, string projectDirectory, CancellationToken cancellationToken = default)
    {
        project.UpdatedAt = DateTimeOffset.UtcNow;
        var projectPath = Path.Combine(Path.GetFullPath(projectDirectory), "project.json");
        if (File.Exists(projectPath))
        {
            var versions = Path.Combine(projectDirectory, "versions");
            Directory.CreateDirectory(versions);
            File.Copy(projectPath, Path.Combine(versions, $"project-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}.json"), false);
            foreach (var file in new DirectoryInfo(versions).EnumerateFiles("project-*.json").OrderByDescending(file => file.CreationTimeUtc).Skip(30)) file.Delete();
        }
        await JsonFiles.WriteAtomicAsync(projectPath, project, cancellationToken);
    }

    public async Task<MediaItem> ImportMediaAsync(ProjectDocument project, string projectDirectory, string sourceFile, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourceFile)) throw new FileNotFoundException("ไม่พบไฟล์ต้นฉบับ", sourceFile);
        var sourceDirectory = Path.Combine(Path.GetFullPath(projectDirectory), "source");
        Directory.CreateDirectory(sourceDirectory);
        var destination = UniqueFile(Path.Combine(sourceDirectory, Path.GetFileName(sourceFile)));
        await CopyFileAsync(sourceFile, destination, cancellationToken);
        try
        {
            var result = await probe.ProbeAsync(destination, cancellationToken);
            var item = new MediaItem
            {
                OriginalPath = Path.GetFullPath(sourceFile), ProjectPath = Path.GetRelativePath(projectDirectory, destination),
                FileName = Path.GetFileName(destination), FileSize = new FileInfo(destination).Length,
                VideoCodec = result.VideoCodec, AudioCodec = result.AudioCodec, Width = result.Width, Height = result.Height,
                FrameRate = result.FrameRate, Duration = result.Duration, HasAudio = result.HasAudio
            };
            project.SourceFiles.Add(item);
            var track = project.Tracks[0];
            var timelineStart = track.Clips.Select(clip => clip.TimelineStart + clip.Duration).DefaultIfEmpty(TimeSpan.Zero).Max();
            track.Clips.Add(new TimelineClip
            {
                MediaId = item.Id, DisplayName = item.FileName, SourceIn = TimeSpan.Zero,
                SourceOut = item.Duration, TimelineStart = timelineStart
            });
            project.ProcessingHistory.Add(new ProcessingHistoryItem { Action = "import", Result = item.FileName });
            await SaveAsync(project, projectDirectory, cancellationToken);
            return item;
        }
        catch
        {
            File.Delete(destination);
            throw;
        }
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, true);
        await input.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private static string UniqueDirectory(string path)
    {
        if (!Directory.Exists(path)) return path;
        for (var index = 2; ; index++) if (!Directory.Exists($"{path} ({index})")) return $"{path} ({index})";
    }

    private static string UniqueFile(string path)
    {
        if (!File.Exists(path)) return path;
        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var index = 2; ; index++)
        {
            var candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }
}

public sealed partial class FfmpegExportService
{
    private readonly string _ffmpegPath;
    public FfmpegExportService(string? ffmpegPath = null) => _ffmpegPath = ffmpegPath ?? ToolLocator.Resolve("ffmpeg");

    public async Task ExportAsync(ExportRequest request, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(request.InputPath)) throw new FileNotFoundException("ไม่พบไฟล์ต้นฉบับสำหรับ Export", request.InputPath);
        if (request.Duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(request), "ช่วง Export ต้องมากกว่า 0 วินาที");
        if (Path.GetFullPath(request.InputPath).Equals(Path.GetFullPath(request.OutputPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("ห้ามเขียนทับไฟล์ต้นฉบับ");
        if (File.Exists(request.OutputPath)) throw new IOException("ไฟล์ปลายทางมีอยู่แล้ว โปรดเลือกชื่อใหม่");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.OutputPath))!);
        var temporaryOutput = request.OutputPath + ".partial" + Path.GetExtension(request.OutputPath);
        if (File.Exists(temporaryOutput)) File.Delete(temporaryOutput);
        var startInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath, RedirectStandardError = true, RedirectStandardOutput = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        foreach (var argument in new[]
        {
            "-hide_banner", "-nostdin", "-y", "-ss", FormatTime(request.Start), "-i", request.InputPath,
            "-t", FormatTime(request.Duration), "-map", "0:v:0", "-map", "0:a?", "-c:v", request.Settings.Codec,
            "-preset", "medium", "-crf", request.Settings.Crf.ToString(CultureInfo.InvariantCulture),
            "-pix_fmt", "yuv420p", "-c:a", request.Settings.AudioCodec,
            "-b:a", $"{request.Settings.AudioBitrateKbps}k", "-movflags", "+faststart",
            "-progress", "pipe:2", temporaryOutput
        }) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        try { process.Start(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException("เปิด FFmpeg ไม่ได้ โปรดวาง ffmpeg.exe ในโฟลเดอร์ tools หรือเพิ่ม FFmpeg ลง PATH", ex);
        }

        var recent = new Queue<string>();
        while (await process.StandardError.ReadLineAsync(cancellationToken) is { } line)
        {
            recent.Enqueue(line);
            while (recent.Count > 40) recent.Dequeue();
            var match = OutTimeRegex().Match(line);
            if (match.Success && TimeSpan.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var current))
                progress?.Report((int)Math.Clamp(current.TotalMilliseconds / request.Duration.TotalMilliseconds * 100, 0, 99));
        }
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            if (File.Exists(temporaryOutput)) File.Delete(temporaryOutput);
            throw new InvalidOperationException($"FFmpeg Export ไม่สำเร็จ (Exit {process.ExitCode}){Environment.NewLine}{string.Join(Environment.NewLine, recent)}");
        }
        File.Move(temporaryOutput, request.OutputPath, false);
        progress?.Report(100);
    }

    private static string FormatTime(TimeSpan value) => value.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
    [GeneratedRegex(@"^out_time=(\d{2}:\d{2}:\d{2}\.\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex OutTimeRegex();
}

public sealed class PersistentJobQueue
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public PersistentJobQueue(string? jobsRoot = null)
    {
        JobsRoot = jobsRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoCutStudio", "Jobs");
        Directory.CreateDirectory(JobsRoot);
    }
    public string JobsRoot { get; }

    public async Task<JobRecord> EnqueueAsync(ExportRequest request, CancellationToken cancellationToken = default)
    {
        var job = new JobRecord { JobId = request.JobId, Export = request, Status = JobStatus.Waiting };
        var directory = JobDirectory(job.JobId);
        Directory.CreateDirectory(directory);
        await JsonFiles.WriteAtomicAsync(Path.Combine(directory, "job.json"), job, cancellationToken);
        await JsonFiles.WriteAtomicAsync(Path.Combine(directory, "progress.json"), new { status = job.Status, percent = 0 }, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory, "run.log"), $"{DateTimeOffset.Now:O} job queued{Environment.NewLine}", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory, "error.log"), string.Empty, cancellationToken);
        return job;
    }

    public async Task<JobRecord?> TryClaimNextAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var path in Directory.EnumerateFiles(JobsRoot, "job.json", SearchOption.AllDirectories).OrderBy(File.GetCreationTimeUtc))
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024, true);
                var job = await JsonSerializer.DeserializeAsync<JobRecord>(stream, JsonFiles.Options, cancellationToken);
                if (job?.Status is not (JobStatus.Waiting or JobStatus.Resumable)) continue;
                job.Status = JobStatus.Preparing;
                await UpdateAsync(job, cancellationToken);
                return job;
            }
            return null;
        }
        finally { _gate.Release(); }
    }

    public async Task UpdateAsync(JobRecord job, CancellationToken cancellationToken = default)
    {
        job.UpdatedAt = DateTimeOffset.UtcNow;
        var directory = JobDirectory(job.JobId);
        await JsonFiles.WriteAtomicAsync(Path.Combine(directory, "job.json"), job, cancellationToken);
        await JsonFiles.WriteAtomicAsync(Path.Combine(directory, "progress.json"), new
        {
            status = job.Status, percent = job.ProgressPercent, updatedAt = job.UpdatedAt, error = job.ErrorMessage
        }, cancellationToken);
    }

    public Task AppendLogAsync(Guid jobId, string message, bool error, CancellationToken cancellationToken = default) =>
        File.AppendAllTextAsync(Path.Combine(JobDirectory(jobId), error ? "error.log" : "run.log"),
            $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}", cancellationToken);

    public string JobDirectory(Guid jobId) => Path.Combine(JobsRoot, jobId.ToString("N"));
}

public static class JobWorker
{
    public static async Task RunAsync(bool runOnce, CancellationToken cancellationToken = default)
    {
        var queue = new PersistentJobQueue();
        var exporter = new FfmpegExportService();
        while (!cancellationToken.IsCancellationRequested)
        {
            var job = await queue.TryClaimNextAsync(cancellationToken);
            if (job is null)
            {
                if (runOnce) return;
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                continue;
            }
            if (job.Export is null)
            {
                job.Status = JobStatus.Failed;
                job.ErrorMessage = "Job ไม่มีข้อมูล Export";
                await queue.UpdateAsync(job, cancellationToken);
                if (runOnce) return;
                continue;
            }
            try
            {
                job.Status = JobStatus.Exporting;
                await queue.UpdateAsync(job, cancellationToken);
                await queue.AppendLogAsync(job.JobId, $"Export started: {job.Export.InputPath}", false, cancellationToken);
                var progress = new Progress<int>(async percent =>
                {
                    job.ProgressPercent = percent;
                    try { await queue.UpdateAsync(job); } catch { }
                });
                await exporter.ExportAsync(job.Export, progress, cancellationToken);
                job.ProgressPercent = 100;
                job.Status = JobStatus.Completed;
                await queue.UpdateAsync(job, cancellationToken);
                await queue.AppendLogAsync(job.JobId, $"Export completed: {job.Export.OutputPath}", false, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                job.Status = JobStatus.Cancelled;
                job.ErrorMessage = "งานถูกยกเลิก";
                await queue.UpdateAsync(job, CancellationToken.None);
            }
            catch (Exception ex)
            {
                job.Status = JobStatus.Failed;
                job.ErrorMessage = ex.Message;
                await queue.UpdateAsync(job, CancellationToken.None);
                await queue.AppendLogAsync(job.JobId, ex.ToString(), true, CancellationToken.None);
            }
            if (runOnce) return;
        }
    }
}
