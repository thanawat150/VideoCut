namespace AutoCutStudio.Core.Models;

public static class AutomaticEditActions
{
    public const string RemoveSilence = "remove_silence";
    public const string TranscribeSpeech = "transcribe_speech";
    public const string CreateSubtitles = "create_subtitles";
    public const string RemoveFillerWords = "remove_filler_words";
}

public sealed record AutomaticEditRequest
{
    public string OriginalCommand { get; init; } = string.Empty;
    public bool IsSupported { get; init; }
    public string? Action { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed record SilenceDetectionOptions
{
    public string PresetId { get; init; } = "balanced";
    public string DisplayName { get; init; } = "สมดุล";
    public double NoiseThresholdDb { get; init; } = -35;
    public double MinimumSilenceSeconds { get; init; } = 0.5;
    public double EdgePaddingSeconds { get; init; } = 0.12;
    public double MinimumOutputSegmentSeconds { get; init; } = 0.08;
}

public sealed record SilenceInterval
{
    public double StartSeconds { get; init; }
    public double EndSeconds { get; init; }
    public double DurationSeconds => Math.Max(0, EndSeconds - StartSeconds);
}

public sealed record SilenceDetectionResult
{
    public string InputPath { get; init; } = string.Empty;
    public double SourceDurationSeconds { get; init; }
    public SilenceDetectionOptions Options { get; init; } = new();
    public IReadOnlyList<SilenceInterval> Intervals { get; init; } = [];
    public string SafeCommandDisplay { get; init; } = string.Empty;
    public string TechnicalLogTail { get; init; } = string.Empty;
    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record AutomaticEditPlan
{
    public Guid PlanId { get; init; } = Guid.NewGuid();
    public string Command { get; init; } = string.Empty;
    public string Action { get; init; } = AutomaticEditActions.RemoveSilence;
    public string InputPath { get; init; } = string.Empty;
    public SilenceDetectionOptions Options { get; init; } = new();
    public IReadOnlyList<SilenceInterval> DetectedSilence { get; init; } = [];
    public IReadOnlyList<TimelineSegment> ProposedSegments { get; init; } = [];
    public double SourceDurationSeconds { get; init; }
    public double OutputDurationSeconds { get; init; }
    public double RemovedDurationSeconds { get; init; }
    public bool IsActionable { get; init; }
    public List<string> Warnings { get; init; } = [];
    public string DetectionCommand { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
