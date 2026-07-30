namespace AutoCutStudio.Core.Models;

public static class AdvancedJobTypes
{
    public const string PrivacyBlurExport = "privacy_blur_export";
    public const string TemplateVideoExport = "template_video_export";
}

public sealed record ComputerVisionToolAvailability(
    bool IsReady,
    string? FaceModelPath,
    string? ObjectModelPath,
    string Status,
    string Message);

public sealed record ObjectDetection
{
    public double TimeSeconds { get; init; }
    public int ClassId { get; init; }
    public string Label { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
}

public sealed record ObjectAnalysisResult
{
    public string SourcePath { get; init; } = string.Empty;
    public double SampleIntervalSeconds { get; init; }
    public List<ObjectDetection> Detections { get; init; } = [];
    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record DetectedFace
{
    public double TimeSeconds { get; init; }
    public double Confidence { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
}

public sealed record FaceTrackKeyframe
{
    public double TimeSeconds { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public double Confidence { get; init; }
}

public sealed record FaceTrack
{
    public Guid TrackId { get; init; } = Guid.NewGuid();
    public int DisplayIndex { get; init; }
    public List<FaceTrackKeyframe> Keyframes { get; init; } = [];
    public double StartSeconds => Keyframes.Count == 0 ? 0 : Keyframes.Min(item => item.TimeSeconds);
    public double EndSeconds => Keyframes.Count == 0 ? 0 : Keyframes.Max(item => item.TimeSeconds);
    public double AverageConfidence => Keyframes.Count == 0 ? 0 : Keyframes.Average(item => item.Confidence);
    public bool IsSelected { get; set; } = true;
}

public sealed record FaceTrackingResult
{
    public string SourcePath { get; init; } = string.Empty;
    public int SourceWidth { get; init; }
    public int SourceHeight { get; init; }
    public double SampleIntervalSeconds { get; init; } = 0.5;
    public List<FaceTrack> Tracks { get; init; } = [];
    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record PrivacyBlurPlan
{
    public Guid PlanId { get; init; } = Guid.NewGuid();
    public List<FaceTrack> SelectedTracks { get; init; } = [];
    public int BlurStrength { get; init; } = 18;
    public double BoxPaddingRatio { get; init; } = 0.18;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record BrollSuggestion
{
    public int Rank { get; init; }
    public string Keyword { get; init; } = string.Empty;
    public string SuggestedShot { get; init; } = string.Empty;
    public double StartSeconds { get; init; }
    public double EndSeconds { get; init; }
    public string? MatchedLocalAsset { get; init; }
    public List<string> Reasons { get; init; } = [];
    public string ReasonSummary => string.Join("; ", Reasons);
}

public sealed record DocumentSection
{
    public int Index { get; init; }
    public string Heading { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public double SuggestedDurationSeconds { get; init; } = 5;
}

public sealed record TemplateVideoRecipe
{
    public Guid RecipeId { get; init; } = Guid.NewGuid();
    public string Title { get; init; } = string.Empty;
    public int Width { get; init; } = 1920;
    public int Height { get; init; } = 1080;
    public int FrameRate { get; init; } = 30;
    public string ThemeId { get; init; } = "documentary_dark";
    public List<DocumentSection> Sections { get; init; } = [];
    public bool GenerateWindowsVoiceover { get; init; }
    public string VoiceLanguage { get; init; } = "auto";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record PrivacyBlurRecipe
{
    public List<FaceTrack> Tracks { get; init; } = [];
    public int BlurStrength { get; init; } = 18;
    public double PaddingRatio { get; init; } = 0.18;
    public double SampleIntervalSeconds { get; init; } = 0.5;
}

public sealed record TemplateVideoJobRecipe
{
    public TemplateVideoRecipe Recipe { get; init; } = new();
    public string ScriptPath { get; init; } = string.Empty;
    public string? VoiceoverPath { get; init; }
    public string AssPath { get; init; } = string.Empty;
}
