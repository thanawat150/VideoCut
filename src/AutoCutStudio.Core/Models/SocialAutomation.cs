namespace AutoCutStudio.Core.Models;

public static class JobTypes
{
    public const string TimelineExport = "timeline_export";
    public const string SocialClipExport = "social_clip_export";
    public const string EnhancedExport = "enhanced_export";
    public const string MultiClipExport = "multi_clip_export";
}

public sealed record SocialExportPreset
{
    public string Id { get; init; } = "tiktok";
    public string PlatformId { get; init; } = "tiktok";
    public string DisplayName { get; init; } = "TikTok";
    public int Width { get; init; } = 1080;
    public int Height { get; init; } = 1920;
    public int FrameRate { get; init; } = 30;
    public string AspectStrategy { get; init; } = "center_crop";
    public int VideoBitrateKbps { get; init; } = 8000;
    public int AudioBitrateKbps { get; init; } = 192;
    public double SuggestedMaximumDurationSeconds { get; init; } = 60;
    public string Pacing { get; init; } = "fast";
    public bool EnableHook { get; init; } = true;
    public bool EnableCta { get; init; } = true;
    public CaptionSafeZone CaptionSafeZone { get; init; } = new();
}

public sealed record RenderRecipe
{
    public string PresetId { get; init; } = "source";
    public int? OutputWidth { get; init; }
    public int? OutputHeight { get; init; }
    public int? OutputFrameRate { get; init; }
    public string AspectStrategy { get; init; } = "source";
    public int VideoBitrateKbps { get; init; } = 8000;
    public int AudioBitrateKbps { get; init; } = 192;
    public string? CaptionAssPath { get; init; }
    public bool BurnCaptions { get; init; }
    public string? HookText { get; init; }
    public string? CtaText { get; init; }
    public CaptionSafeZone CaptionSafeZone { get; init; } = new();
    public List<BrollOverlayRecipe> BrollOverlays { get; init; } = [];
    public string AudioEnhancementPreset { get; init; } = AudioEnhancementPresets.None;
    public string ColorPreset { get; init; } = ColorPresets.None;
    public bool Stabilize { get; init; }
    public string? MusicPath { get; init; }
    public bool EnableMusicDucking { get; init; }
    public double MusicVolume { get; init; } = 0.28;
}

public sealed record HighlightCandidate
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public int Rank { get; init; }
    public double StartSeconds { get; init; }
    public double EndSeconds { get; init; }
    public double DurationSeconds => Math.Max(0, EndSeconds - StartSeconds);
    public double Score { get; init; }
    public string PreviewText { get; init; } = string.Empty;
    public List<string> Reasons { get; init; } = [];
    public string ReasonSummary => string.Join("; ", Reasons);
    public bool IsSelected { get; set; }
}

public sealed record HighlightAnalysisOptions
{
    public double TargetDurationSeconds { get; init; } = 35;
    public double MinimumDurationSeconds { get; init; } = 12;
    public double MaximumDurationSeconds { get; init; } = 60;
    public int MaximumCandidates { get; init; } = 8;
    public double ContextPaddingSeconds { get; init; } = 0.35;
}

public sealed record SocialClipPlan
{
    public Guid PlanId { get; init; } = Guid.NewGuid();
    public Guid ProjectId { get; init; }
    public Guid MediaAssetId { get; init; }
    public SocialExportPreset Preset { get; init; } = new();
    public List<HighlightCandidate> Candidates { get; init; } = [];
    public string HookText { get; init; } = string.Empty;
    public string CtaText { get; init; } = string.Empty;
    public bool BurnCaptions { get; init; } = true;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
