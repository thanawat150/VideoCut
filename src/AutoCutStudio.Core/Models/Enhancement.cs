namespace AutoCutStudio.Core.Models;

public static class AudioEnhancementPresets
{
    public const string None = "none";
    public const string NoiseReduction = "noise_reduction";
    public const string VoiceEnhance = "voice_enhance";
    public const string NoiseAndVoice = "noise_and_voice";
}

public static class ColorPresets
{
    public const string None = "none";
    public const string Natural = "natural";
    public const string Vivid = "vivid";
    public const string Warm = "warm";
    public const string Cool = "cool";
}

public sealed record EnhancementPlan
{
    public Guid PlanId { get; init; } = Guid.NewGuid();
    public string AudioPreset { get; init; } = AudioEnhancementPresets.None;
    public string ColorPreset { get; init; } = ColorPresets.None;
    public bool Stabilize { get; init; }
    public string? MusicPath { get; init; }
    public bool EnableMusicDucking { get; init; }
    public double MusicVolume { get; init; } = 0.28;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record BeatMarker
{
    public int Index { get; init; }
    public double TimeSeconds { get; init; }
    public double Strength { get; init; }
}

public sealed record BeatAnalysisResult
{
    public string InputPath { get; init; } = string.Empty;
    public double DurationSeconds { get; init; }
    public double EstimatedBpm { get; init; }
    public List<BeatMarker> Beats { get; init; } = [];
    public string Method { get; init; } = "PCM RMS onset peaks";
    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.UtcNow;
}
