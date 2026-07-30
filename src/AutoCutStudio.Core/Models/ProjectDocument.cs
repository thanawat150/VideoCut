namespace AutoCutStudio.Core.Models;

public sealed record MediaAsset
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string SourcePath { get; init; } = string.Empty;
    public MediaMetadata Metadata { get; init; } = new();
    public DateTimeOffset ImportedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record OutputHistoryItem
{
    public Guid JobId { get; init; }
    public string OutputPath { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record ProjectDocument
{
    public Guid ProjectId { get; init; } = Guid.NewGuid();
    public string DisplayName { get; init; } = "Untitled";
    public string RootPath { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedAt { get; init; } = DateTimeOffset.UtcNow;
    public string ApplicationVersion { get; init; } = "0.1.0";
    public List<MediaAsset> SourceMedia { get; init; } = [];
    public TimelineDocument Timeline { get; init; } = new();
    public List<Guid> JobHistory { get; init; } = [];
    public List<OutputHistoryItem> OutputHistory { get; init; } = [];
}
