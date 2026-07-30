namespace AutoCutStudio.Core.Models;

public static class ProfessionalJobTypes
{
    public const string MulticamExport = "multicam_export";
    public const string KeyframeExport = "keyframe_export";
    public const string NestedSequenceExport = "nested_sequence_export";
}

public sealed record CameraAngle
{
    public Guid AngleId { get; init; } = Guid.NewGuid();
    public string DisplayName { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public bool HasAudio { get; init; }
    public double OffsetSeconds { get; init; }
}

public sealed record MulticamSwitch
{
    public int Sequence { get; init; }
    public Guid AngleId { get; init; }
    public double SourceStartSeconds { get; init; }
    public double SourceEndSeconds { get; init; }
    public double DurationSeconds => Math.Max(0, SourceEndSeconds - SourceStartSeconds);
}

public sealed record MulticamRenderRecipe
{
    public List<CameraAngle> Angles { get; init; } = [];
    public List<MulticamSwitch> Switches { get; init; } = [];
    public int OutputWidth { get; init; } = 1920;
    public int OutputHeight { get; init; } = 1080;
    public int FrameRate { get; init; } = 30;
}

public sealed record MotionKeyframe
{
    public int Sequence { get; init; }
    public double TimeSeconds { get; init; }
    public double Zoom { get; init; } = 1;
    public double FocusX { get; init; } = 0.5;
    public double FocusY { get; init; } = 0.5;
    public double AudioGain { get; init; } = 1;
}

public sealed record KeyframeRenderRecipe
{
    public List<MotionKeyframe> Keyframes { get; init; } = [];
    public int OutputWidth { get; init; } = 1920;
    public int OutputHeight { get; init; } = 1080;
    public int FrameRate { get; init; } = 30;
    public string Interpolation { get; init; } = "linear";
}

public sealed record NestedSequenceClip
{
    public int Sequence { get; init; }
    public string SourcePath { get; init; } = string.Empty;
    public double SourceStartSeconds { get; init; }
    public double SourceEndSeconds { get; init; }
    public bool HasAudio { get; init; }
    public double DurationSeconds => Math.Max(0, SourceEndSeconds - SourceStartSeconds);
}

public sealed record NestedSequenceDocument
{
    public Guid SequenceId { get; init; } = Guid.NewGuid();
    public Guid ProjectId { get; init; }
    public string Name { get; init; } = "Nested Sequence";
    public List<NestedSequenceClip> Clips { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record NestedSequenceRenderRecipe
{
    public Guid SequenceId { get; init; }
    public string Name { get; init; } = string.Empty;
    public List<NestedSequenceClip> Clips { get; init; } = [];
    public int OutputWidth { get; init; } = 1920;
    public int OutputHeight { get; init; } = 1080;
    public int FrameRate { get; init; } = 30;
}

public sealed record PluginExportPreset
{
    public int Width { get; init; } = 1920;
    public int Height { get; init; } = 1080;
    public int FrameRate { get; init; } = 30;
    public int VideoBitrateKbps { get; init; } = 8000;
    public int AudioBitrateKbps { get; init; } = 192;
}

public sealed record PluginManifest
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = "1.0.0";
    public string Type { get; init; } = "export_preset";
    public string Description { get; init; } = string.Empty;
    public PluginExportPreset? ExportPreset { get; init; }
    public string ManifestPath { get; init; } = string.Empty;
    public bool IsValid { get; init; }
    public string ValidationMessage { get; init; } = string.Empty;
}

public sealed record ProviderCapability
{
    public string ProviderId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public bool IsConfigured { get; init; }
    public bool CanPublishDirectly { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed record DeliveryPackageManifest
{
    public Guid PackageId { get; init; } = Guid.NewGuid();
    public string ProviderId { get; init; } = "local-folder";
    public string SourcePath { get; init; } = string.Empty;
    public string DeliveredPath { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record SocialPublishMetadata
{
    public string Platform { get; init; } = "youtube";
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public List<string> Tags { get; init; } = [];
    public string Privacy { get; init; } = "private";
}

public sealed record CollaborationPackageManifest
{
    public Guid PackageId { get; init; } = Guid.NewGuid();
    public Guid ProjectId { get; init; }
    public string ProjectName { get; init; } = string.Empty;
    public bool IncludesSourceMedia { get; init; }
    public List<CollaborationPackageEntry> Entries { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record CollaborationPackageEntry
{
    public string RelativePath { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
}
