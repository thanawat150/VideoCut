namespace AutoCutStudio.Core.Models;

public static class JobStatuses
{
    public const string Draft = "draft";
    public const string Queued = "queued";
    public const string Preparing = "preparing";
    public const string Processing = "processing";
    public const string Exporting = "exporting";
    public const string Completed = "completed";
    public const string CompletedWithWarnings = "completed_with_warnings";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string Paused = "paused";
    public const string Resumable = "resumable";
    public const string Retrying = "retrying";
}

public sealed record JobDocument
{
    public Guid JobId { get; init; } = Guid.NewGuid();
    public Guid ProjectId { get; init; }
    public string ProjectRoot { get; init; } = string.Empty;
    public string JobType { get; init; } = JobTypes.TimelineExport;
    public string Status { get; init; } = JobStatuses.Draft;
    public int Priority { get; init; }
    public string InputPath { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
    public List<TimelineSegment> Segments { get; init; } = [];
    public bool ExpectedInputHasAudio { get; init; }
    public double ExpectedDurationSeconds { get; init; }
    public RenderRecipe? RenderRecipe { get; init; }
    public PrivacyBlurRecipe? PrivacyBlurRecipe { get; init; }
    public TemplateVideoJobRecipe? TemplateVideoRecipe { get; init; }
    public MulticamRenderRecipe? MulticamRecipe { get; init; }
    public KeyframeRenderRecipe? KeyframeRecipe { get; init; }
    public NestedSequenceRenderRecipe? NestedSequenceRecipe { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public int? WorkerProcessId { get; init; }
    public string? FailureMessage { get; init; }
}

public sealed record JobProgress
{
    public Guid JobId { get; init; }
    public string Status { get; init; } = JobStatuses.Queued;
    public double Progress { get; init; }
    public double? EstimatedRemainingSeconds { get; init; }
    public string? ActiveAgentId { get; init; }
    public string Message { get; init; } = string.Empty;
    public int? WorkerProcessId { get; init; }
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record JobControl
{
    public string RequestedAction { get; init; } = "none";
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record ProcessingReport
{
    public Guid JobId { get; init; }
    public string InputPath { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
    public IReadOnlyList<TimelineSegment> Segments { get; init; } = [];
    public string Encoder { get; init; } = "libx264";
    public string AudioEncoder { get; init; } = "aac";
    public string SafeCommandDisplay { get; init; } = string.Empty;
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
    public bool SourceWasModified { get; init; }
}

public sealed record QaReport
{
    public Guid JobId { get; init; }
    public bool Passed { get; init; }
    public bool OutputExists { get; init; }
    public bool HasVideo { get; init; }
    public bool HasAudio { get; init; }
    public bool ExpectedAudio { get; init; }
    public double ExpectedDurationSeconds { get; init; }
    public double ActualDurationSeconds { get; init; }
    public long FileSizeBytes { get; init; }
    public List<string> Errors { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record ExportManifest
{
    public Guid JobId { get; init; }
    public Guid ProjectId { get; init; }
    public string InputPath { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public double DurationSeconds { get; init; }
    public bool HasVideo { get; init; }
    public bool HasAudio { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
