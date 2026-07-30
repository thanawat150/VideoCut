namespace AutoCutStudio.Core.Models;

public sealed record TimelineSegment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public double StartSeconds { get; init; }
    public double EndSeconds { get; init; }

    public double DurationSeconds => Math.Max(0, EndSeconds - StartSeconds);
}

public sealed record TimelineDocument
{
    public Guid? SourceMediaId { get; init; }
    public List<TimelineSegment> Segments { get; init; } = [];
    public DateTimeOffset ModifiedAt { get; init; } = DateTimeOffset.UtcNow;

    public double OutputDurationSeconds => Segments.Sum(segment => segment.DurationSeconds);
}
