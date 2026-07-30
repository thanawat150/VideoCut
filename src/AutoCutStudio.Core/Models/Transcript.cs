namespace AutoCutStudio.Core.Models;

public sealed record TranscriptSegment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public int Sequence { get; init; }
    public double StartSeconds { get; init; }
    public double EndSeconds { get; init; }
    public string Text { get; set; } = string.Empty;
    public string OriginalText { get; init; } = string.Empty;
    public bool IsExcluded { get; set; }
    public bool IsFiller { get; set; }
    public double? AverageProbability { get; init; }
    public double DurationSeconds => Math.Max(0, EndSeconds - StartSeconds);
}

public sealed record TranscriptDocument
{
    public Guid TranscriptId { get; init; } = Guid.NewGuid();
    public Guid ProjectId { get; init; }
    public Guid MediaAssetId { get; init; }
    public string SourcePath { get; init; } = string.Empty;
    public string Language { get; set; } = "auto";
    public string DetectedLanguage { get; init; } = string.Empty;
    public string ModelName { get; init; } = string.Empty;
    public List<TranscriptSegment> Segments { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;
    public string PlainText => string.Join(
        Environment.NewLine,
        Segments.Where(item => !item.IsExcluded).Select(item => item.Text.Trim()));
}

public sealed record SpeechToolAvailability(
    bool IsReady,
    string? WhisperCliPath,
    string? ModelPath,
    string Status,
    string Message);

public sealed record TranscriptionOptions
{
    public string Language { get; init; } = "auto";
    public int Threads { get; init; } = Math.Max(2, Environment.ProcessorCount / 2);
    public bool UseGpu { get; init; } = true;
    public string? InitialPrompt { get; init; }
}

public sealed record TranscriptionResult
{
    public TranscriptDocument Transcript { get; init; } = new();
    public string JsonPath { get; init; } = string.Empty;
    public string SrtPath { get; init; } = string.Empty;
    public string TextPath { get; init; } = string.Empty;
    public string AudioPath { get; init; } = string.Empty;
    public string SafeWhisperCommand { get; init; } = string.Empty;
    public string TechnicalLogTail { get; init; } = string.Empty;
}
