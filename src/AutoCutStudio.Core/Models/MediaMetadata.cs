namespace AutoCutStudio.Core.Models;

public sealed record MediaMetadata
{
    public string SourcePath { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public string Container { get; init; } = string.Empty;
    public string? VideoCodec { get; init; }
    public string? AudioCodec { get; init; }
    public bool HasVideo { get; init; }
    public bool HasAudio { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public double FrameRate { get; init; }
    public double DurationSeconds { get; init; }
    public long? BitRate { get; init; }
    public int? AudioChannels { get; init; }
    public int? AudioSampleRate { get; init; }
    public int Rotation { get; init; }
    public string? PixelFormat { get; init; }
    public string? ColorSpace { get; init; }
    public bool IsVariableFrameRate { get; init; }
    public string ProbeJson { get; init; } = string.Empty;
    public DateTimeOffset ProbedAt { get; init; } = DateTimeOffset.UtcNow;
}
