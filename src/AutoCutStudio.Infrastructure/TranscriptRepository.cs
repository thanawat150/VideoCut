using System.Text;
using System.Text.Json;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;

namespace AutoCutStudio.Infrastructure;

public sealed class TranscriptRepository
{
    public string GetTranscriptPath(string projectRoot) =>
        Path.Combine(projectRoot, "transcript", "transcript.json");

    public string GetSrtPath(string projectRoot) =>
        Path.Combine(projectRoot, "subtitles", "transcript.srt");

    public async Task<TranscriptDocument?> OpenAsync(
        string projectRoot,
        CancellationToken cancellationToken = default)
    {
        var path = GetTranscriptPath(projectRoot);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            32 * 1024,
            FileOptions.Asynchronous);
        return await JsonSerializer.DeserializeAsync<TranscriptDocument>(
            stream,
            JsonDefaults.Options,
            cancellationToken);
    }

    public async Task SaveAsync(
        string projectRoot,
        TranscriptDocument transcript,
        CancellationToken cancellationToken = default)
    {
        var path = GetTranscriptPath(projectRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        transcript.ModifiedAt = DateTimeOffset.UtcNow;
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(
            temporary,
            JsonSerializer.Serialize(transcript, JsonDefaults.Options),
            new UTF8Encoding(false),
            cancellationToken);
        File.Move(temporary, path, true);
    }

    public async Task<string> ExportSrtAsync(
        string projectRoot,
        TranscriptDocument transcript,
        TranscriptEditingService editor,
        CancellationToken cancellationToken = default)
    {
        var path = GetSrtPath(projectRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(
            temporary,
            editor.BuildSrt(transcript),
            new UTF8Encoding(false),
            cancellationToken);
        File.Move(temporary, path, true);
        return path;
    }
}
