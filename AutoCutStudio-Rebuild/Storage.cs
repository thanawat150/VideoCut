using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace AutoCutStudio.Rebuild;

public static class AtomicJson
{
    public static async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            await JsonSerializer.SerializeAsync(stream, value, JsonConfig.Options, cancellationToken);
        File.Move(temporary, path, true);
    }

    public static async Task<T> ReadAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(Path.GetFullPath(path));
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonConfig.Options, cancellationToken)
            ?? throw new InvalidDataException($"Invalid JSON: {path}");
    }
}

public static class Security
{
    public static string ExistingFile(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) throw new FileNotFoundException("Input file does not exist.", full);
        return full;
    }

    public static string OutputUnderProject(string projectRoot, string output)
    {
        var root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(output);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Output must be inside the Project folder.");
        return full;
    }

    public static async Task<string> Sha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
    }
}

public static class VersionedOutput
{
    public static string Next(string directory, string name, string extension)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name + extension);
        if (!File.Exists(path)) return path;
        for (var version = 2; version < 10000; version++)
        {
            path = Path.Combine(directory, $"{name}_v{version}{extension}");
            if (!File.Exists(path)) return path;
        }
        throw new IOException("Could not allocate a versioned output path.");
    }
}

public sealed class TimelineEditor
{
    public TimelineClip Add(ProjectDocument project, MediaAsset media)
    {
        var duration = media.Probe?.DurationSeconds ?? throw new InvalidOperationException("Media must be probed first.");
        var clip = new TimelineClip { MediaId = media.MediaId, SourceInSeconds = 0, SourceOutSeconds = duration, Order = project.Timeline.Count };
        project.Timeline.Add(clip);
        Touch(project);
        return clip;
    }

    public void Trim(ProjectDocument project, Guid clipId, double input, double output)
    {
        if (input < 0 || output <= input) throw new ArgumentOutOfRangeException(nameof(output));
        var clip = project.Timeline.Single(x => x.ClipId == clipId);
        clip.SourceInSeconds = input;
        clip.SourceOutSeconds = output;
        Touch(project);
    }

    public TimelineClip Split(ProjectDocument project, Guid clipId, double time)
    {
        var clip = project.Timeline.Single(x => x.ClipId == clipId);
        if (time <= clip.SourceInSeconds || time >= clip.SourceOutSeconds) throw new ArgumentOutOfRangeException(nameof(time));
        var right = new TimelineClip { MediaId = clip.MediaId, SourceInSeconds = time, SourceOutSeconds = clip.SourceOutSeconds, Order = clip.Order + 1, Enabled = clip.Enabled };
        clip.SourceOutSeconds = time;
        foreach (var item in project.Timeline.Where(x => x.Order > clip.Order)) item.Order++;
        project.Timeline.Add(right);
        Normalize(project);
        return right;
    }

    public void Delete(ProjectDocument project, Guid clipId) { project.Timeline.RemoveAll(x => x.ClipId == clipId); Normalize(project); }

    public void Move(ProjectDocument project, Guid clipId, int target)
    {
        var items = project.Timeline.OrderBy(x => x.Order).ToList();
        var clip = items.Single(x => x.ClipId == clipId);
        items.Remove(clip);
        items.Insert(Math.Clamp(target, 0, items.Count), clip);
        for (var index = 0; index < items.Count; index++) items[index].Order = index;
        Touch(project);
    }

    private static void Normalize(ProjectDocument project)
    {
        var items = project.Timeline.OrderBy(x => x.Order).ToList();
        for (var index = 0; index < items.Count; index++) items[index].Order = index;
        Touch(project);
    }

    private static void Touch(ProjectDocument project) => project.ModifiedAt = DateTimeOffset.UtcNow;
}

public static class ProjectStore
{
    public static async Task SaveAsync(ProjectDocument project)
    {
        Directory.CreateDirectory(project.RootPath);
        foreach (var name in new[] { "source", "proxy", "cache", "exports", "jobs", "logs", "backups", "subtitles", "transcript", "assets" })
            Directory.CreateDirectory(Path.Combine(project.RootPath, name));
        project.ModifiedAt = DateTimeOffset.UtcNow;
        var path = Path.Combine(project.RootPath, "project.json");
        if (File.Exists(path)) File.Copy(path, Path.Combine(project.RootPath, "backups", $"project-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json"));
        await AtomicJson.WriteAsync(path, project);
    }

    public static Task<ProjectDocument> LoadAsync(string path) => AtomicJson.ReadAsync<ProjectDocument>(path);
}

public static class JobStore
{
    public static string DirectoryFor(JobDocument job) => Path.Combine(job.ProjectRoot, "jobs", job.JobId.ToString("N"));
    public static string JobPath(JobDocument job) => Path.Combine(DirectoryFor(job), "job.json");
    public static Task SaveAsync(JobDocument job) => AtomicJson.WriteAsync(JobPath(job), job);
    public static Task SaveProgressAsync(JobDocument job, ProgressDocument progress) => AtomicJson.WriteAsync(Path.Combine(DirectoryFor(job), "progress.json"), progress);
}

