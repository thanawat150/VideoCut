using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AutoCutStudio.Core.Models;

namespace AutoCutStudio.Core.Services;

public sealed partial class TranscriptEditingService
{
    private static readonly HashSet<string> FillerAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "เอ่อ", "เออ", "อ่า", "อ้่า", "อืม", "อือ", "แบบว่า", "คือว่า", "ก็แบบ",
        "uh", "um", "umm", "erm", "hmm", "mm"
    };

    public IReadOnlyList<TranscriptSegment> Search(TranscriptDocument transcript, string? query)
    {
        var value = query?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return transcript.Segments;
        }

        return transcript.Segments
            .Where(segment => segment.Text.Contains(value, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public int MarkIsolatedFillers(TranscriptDocument transcript, bool excludeFromAudio)
    {
        var count = 0;
        foreach (var segment in transcript.Segments)
        {
            var normalized = NormalizeForFiller(segment.Text);
            var isFiller = !string.IsNullOrWhiteSpace(normalized) &&
                           (FillerAliases.Contains(normalized) ||
                            normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                .All(FillerAliases.Contains));
            segment.IsFiller = isFiller;
            if (isFiller && excludeFromAudio)
            {
                segment.IsExcluded = true;
            }

            if (isFiller)
            {
                count++;
            }
        }

        transcript.ModifiedAt = DateTimeOffset.UtcNow;
        return count;
    }

    public IReadOnlyList<TimelineSegment> BuildTimelineWithoutExcluded(
        TranscriptDocument transcript,
        double sourceDurationSeconds,
        double speechEdgePaddingSeconds = 0.05)
    {
        if (sourceDurationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceDurationSeconds));
        }

        var removals = transcript.Segments
            .Where(segment => segment.IsExcluded)
            .Select(segment => new
            {
                Start = Math.Clamp(segment.StartSeconds + speechEdgePaddingSeconds, 0, sourceDurationSeconds),
                End = Math.Clamp(segment.EndSeconds - speechEdgePaddingSeconds, 0, sourceDurationSeconds)
            })
            .Where(interval => interval.End > interval.Start + 0.01)
            .OrderBy(interval => interval.Start)
            .ToList();

        var merged = new List<(double Start, double End)>();
        foreach (var interval in removals)
        {
            if (merged.Count == 0 || interval.Start > merged[^1].End + 0.001)
            {
                merged.Add((interval.Start, interval.End));
                continue;
            }

            var previous = merged[^1];
            merged[^1] = (previous.Start, Math.Max(previous.End, interval.End));
        }

        var output = new List<TimelineSegment>();
        var cursor = 0d;
        foreach (var interval in merged)
        {
            if (interval.Start > cursor + 0.01)
            {
                output.Add(new TimelineSegment
                {
                    StartSeconds = cursor,
                    EndSeconds = interval.Start
                });
            }

            cursor = Math.Max(cursor, interval.End);
        }

        if (cursor < sourceDurationSeconds - 0.01)
        {
            output.Add(new TimelineSegment
            {
                StartSeconds = cursor,
                EndSeconds = sourceDurationSeconds
            });
        }

        return output;
    }

    public string BuildSrt(TranscriptDocument transcript)
    {
        var builder = new StringBuilder();
        var sequence = 1;
        foreach (var segment in transcript.Segments.Where(item => !item.IsExcluded))
        {
            var text = segment.Text.Trim();
            if (string.IsNullOrWhiteSpace(text) || segment.EndSeconds <= segment.StartSeconds)
            {
                continue;
            }

            builder.AppendLine(sequence.ToString(CultureInfo.InvariantCulture));
            builder.Append(FormatSrtTimestamp(segment.StartSeconds));
            builder.Append(" --> ");
            builder.AppendLine(FormatSrtTimestamp(segment.EndSeconds));
            builder.AppendLine(text);
            builder.AppendLine();
            sequence++;
        }

        return builder.ToString();
    }

    private static string NormalizeForFiller(string value)
    {
        var normalized = PunctuationRegex().Replace(value.ToLowerInvariant(), " ");
        return string.Join(' ', normalized.Split(
            [' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries));
    }

    private static string FormatSrtTimestamp(double seconds)
    {
        var value = TimeSpan.FromSeconds(Math.Max(0, seconds));
        var totalHours = (int)value.TotalHours;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{totalHours:00}:{value.Minutes:00}:{value.Seconds:00},{value.Milliseconds:000}");
    }

    [GeneratedRegex(@"[^\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex PunctuationRegex();
}
