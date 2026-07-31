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

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--worker")
            return Worker.RunAsync(args[1]).GetAwaiter().GetResult();
        if (args.Length == 1 && args[0] == "--doctor")
            return Worker.Doctor();

        var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        return application.Run(new MainWindow());
    }
}

public enum JobState { Draft, Queued, Preparing, Analysing, Transcribing, Rendering, Validating, Completed, Failed, Cancelled, Resumable }
public enum JobKind { ExportTimeline, DetectSilence, Transcribe, BurnSubtitle, CreateShort, EnhanceAudio, MixVoiceover, Stabilize, OverlayLogo, PrivacyBlur }

public sealed record MediaProbe(string Container, string VideoCodec, string? AudioCodec, int Width, int Height, double FrameRate, double DurationSeconds, bool HasVideo, bool HasAudio, long FileSizeBytes, bool IsVariableFrameRate);

public sealed class MediaAsset
{
    public Guid MediaId { get; set; } = Guid.NewGuid();
    public required string SourcePath { get; set; }
    public required string DisplayName { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public MediaProbe? Probe { get; set; }
}

public sealed class TimelineClip
{
    public Guid ClipId { get; set; } = Guid.NewGuid();
    public Guid MediaId { get; set; }
    public double SourceInSeconds { get; set; }
    public double SourceOutSeconds { get; set; }
    public int Order { get; set; }
    public bool Enabled { get; set; } = true;
    [JsonIgnore] public double DurationSeconds => Math.Max(0, SourceOutSeconds - SourceInSeconds);
}

public sealed class ProjectDocument
{
    public int SchemaVersion { get; set; } = 1;
    public Guid ProjectId { get; set; } = Guid.NewGuid();
    public required string DisplayName { get; set; }
    public required string RootPath { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<MediaAsset> Media { get; set; } = [];
    public List<TimelineClip> Timeline { get; set; } = [];
    public List<Guid> JobHistory { get; set; } = [];
    public List<string> OutputHistory { get; set; } = [];
}

public sealed class ClipInput
{
    public required string SourcePath { get; set; }
    public double SourceInSeconds { get; set; }
    public double SourceOutSeconds { get; set; }
}

public sealed class JobDocument
{
    public Guid JobId { get; set; } = Guid.NewGuid();
    public JobKind Kind { get; set; }
    public JobState State { get; set; } = JobState.Draft;
    public double Progress { get; set; }
    public required string ProjectRoot { get; set; }
    public required string OutputPath { get; set; }
    public string? InputPath { get; set; }
    public string? SubtitlePath { get; set; }
    public string? VoiceoverPath { get; set; }
    public string? LogoPath { get; set; }
    public string? WhisperModelPath { get; set; }
    public string WhisperLanguage { get; set; } = "th";
    public List<ClipInput> Clips { get; set; } = [];
    public Dictionary<string, string> Options { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}

public sealed class ProgressDocument
{
    public Guid JobId { get; set; }
    public JobState State { get; set; }
    public double Progress { get; set; }
    public string Step { get; set; } = string.Empty;
    public string? Message { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class JsonConfig
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}

