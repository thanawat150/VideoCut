namespace AutoCutStudio.Core.Models;

public static class VisualNodeTypes
{
    public const string MediaInput = "media_input";
    public const string FolderInput = "folder_input";
    public const string MergeClips = "merge_clips";
    public const string Transcribe = "transcribe";
    public const string RemoveFillers = "remove_fillers";
    public const string RemoveSilence = "remove_silence";
    public const string CreateCaptions = "create_captions";
    public const string AnalyzeHighlights = "analyze_highlights";
    public const string AutoBroll = "auto_broll";
    public const string VisualApproval = "visual_approval";
    public const string Condition = "condition";
    public const string EnhanceAudio = "enhance_audio";
    public const string Stabilize = "stabilize";
    public const string ColorCorrection = "color_correction";
    public const string PlatformStyle = "platform_style";
    public const string CreateShorts = "create_shorts";
    public const string ExportVideo = "export_video";
    public const string Notify = "notify";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        MediaInput,
        FolderInput,
        MergeClips,
        Transcribe,
        RemoveFillers,
        RemoveSilence,
        CreateCaptions,
        AnalyzeHighlights,
        AutoBroll,
        VisualApproval,
        Condition,
        EnhanceAudio,
        Stabilize,
        ColorCorrection,
        PlatformStyle,
        CreateShorts,
        ExportVideo,
        Notify
    };
}

public static class WorkflowRunStatuses
{
    public const string Draft = "draft";
    public const string Queued = "queued";
    public const string Running = "running";
    public const string WaitingForApproval = "waiting_for_approval";
    public const string Completed = "completed";
    public const string CompletedWithWarnings = "completed_with_warnings";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string Skipped = "skipped";
}

public sealed record VisualWorkflowNode
{
    public Guid NodeId { get; init; } = Guid.NewGuid();
    public string NodeType { get; init; } = VisualNodeTypes.MediaInput;
    public string DisplayName { get; init; } = "Node";
    public double X { get; set; }
    public double Y { get; set; }
    public bool IsDisabled { get; set; }
    public Dictionary<string, string> Settings { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record VisualWorkflowConnection
{
    public Guid ConnectionId { get; init; } = Guid.NewGuid();
    public Guid SourceNodeId { get; init; }
    public string SourcePort { get; init; } = "output";
    public Guid TargetNodeId { get; init; }
    public string TargetPort { get; init; } = "input";
}

public sealed record VisualWorkflowDocument
{
    public Guid WorkflowId { get; init; } = Guid.NewGuid();
    public Guid ProjectId { get; init; }
    public string Name { get; set; } = "Untitled automation";
    public string Description { get; set; } = string.Empty;
    public int SchemaVersion { get; init; } = 1;
    public List<VisualWorkflowNode> Nodes { get; init; } = [];
    public List<VisualWorkflowConnection> Connections { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record WorkflowNodeRunState
{
    public Guid NodeId { get; init; }
    public string NodeType { get; init; } = string.Empty;
    public string Status { get; set; } = WorkflowRunStatuses.Queued;
    public double Progress { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? InputSummary { get; set; }
    public string? OutputSummary { get; set; }
    public List<string> OutputPaths { get; init; } = [];
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? FailureMessage { get; set; }
}

public sealed record WorkflowRunDocument
{
    public Guid RunId { get; init; } = Guid.NewGuid();
    public Guid WorkflowId { get; init; }
    public Guid ProjectId { get; init; }
    public string Status { get; set; } = WorkflowRunStatuses.Queued;
    public List<Guid> OrderedNodeIds { get; init; } = [];
    public List<WorkflowNodeRunState> NodeStates { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record WorkflowValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<string> Errors { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public List<Guid> TopologicalOrder { get; init; } = [];
}

public sealed record CaptionSafeZone
{
    public int MarginLeft { get; init; } = 96;
    public int MarginRight { get; init; } = 96;
    public int MarginBottom { get; init; } = 190;
    public int MarginTop { get; init; } = 120;
    public int MaximumLines { get; init; } = 2;
    public double MaximumWidthRatio { get; init; } = 0.82;
    public int PreferredFontSize { get; init; } = 62;
    public int MinimumFontSize { get; init; } = 46;
}

public sealed record CaptionPage
{
    public List<string> Lines { get; init; } = [];
    public int FontSize { get; init; }
    public string Text => string.Join("\\N", Lines);
}

public sealed record BrollOverlayRecipe
{
    public string AssetPath { get; init; } = string.Empty;
    public double StartSeconds { get; init; }
    public double EndSeconds { get; init; }
    public string FitMode { get; init; } = "cover";
    public string Motion { get; init; } = "ken_burns";
    public double Confidence { get; init; }
    public string SourceKind { get; init; } = "local";
    public string Attribution { get; init; } = string.Empty;
}

public sealed record MultiClipInput
{
    public Guid MediaAssetId { get; init; }
    public string SourcePath { get; init; } = string.Empty;
    public int Order { get; init; }
    public double TrimStartSeconds { get; init; }
    public double? TrimEndSeconds { get; init; }
}

public sealed record MultiClipRenderRecipe
{
    public List<MultiClipInput> Inputs { get; init; } = [];
    public string Transition { get; init; } = "cut";
    public double TransitionDurationSeconds { get; init; } = 0.35;
    public bool NormalizeAudio { get; init; } = true;
    public string? IntroPath { get; init; }
    public string? OutroPath { get; init; }
    public string? MusicPath { get; init; }
    public double MusicVolume { get; init; } = 0.22;
}
