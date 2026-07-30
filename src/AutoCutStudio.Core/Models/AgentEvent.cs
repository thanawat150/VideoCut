namespace AutoCutStudio.Core.Models;

public static class AgentIds
{
    public const string Producer = "producer_agent";
    public const string MediaAnalyst = "media_analyst_agent";
    public const string VideoEditor = "video_editor_agent";
    public const string QualityControl = "quality_control_agent";
    public const string Render = "render_agent";
}

public static class AgentStatuses
{
    public const string Idle = "idle";
    public const string Reading = "reading";
    public const string Thinking = "thinking";
    public const string Analysing = "analysing";
    public const string Editing = "editing";
    public const string Rendering = "rendering";
    public const string WaitingForDependency = "waiting_for_dependency";
    public const string WaitingForUser = "waiting_for_user";
    public const string Warning = "warning";
    public const string Error = "error";
    public const string Completed = "completed";
    public const string Paused = "paused";
    public const string Cancelled = "cancelled";
    public const string Retrying = "retrying";
}

public sealed record AgentEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public string EventType { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public Guid ProjectId { get; init; }
    public Guid? JobId { get; init; }
    public string? AgentId { get; init; }
    public string Action { get; init; } = string.Empty;
    public string Status { get; init; } = AgentStatuses.Idle;
    public double? Progress { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? InputPath { get; init; }
    public string? OutputPath { get; init; }
    public string Severity { get; init; } = "info";
    public bool RequiresUserAction { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
