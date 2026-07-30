using System.Text.RegularExpressions;
using AutoCutStudio.Core.Models;

namespace AutoCutStudio.Core.Services;

public sealed partial class HighlightAnalyzer
{
    private static readonly string[] StrongThaiTerms =
    [
        "สำคัญ", "สรุป", "วิธี", "เหตุผล", "ผลลัพธ์", "ข้อควรระวัง", "เคล็ดลับ",
        "อย่าลืม", "ต้องรู้", "ดีที่สุด", "ปัญหา", "แก้", "จริงๆ", "ทำไม"
    ];

    private static readonly string[] StrongEnglishTerms =
    [
        "important", "summary", "how to", "why", "result", "warning", "tip",
        "remember", "must", "best", "problem", "solution", "secret"
    ];

    public IReadOnlyList<HighlightCandidate> Analyze(
        TranscriptDocument transcript,
        double sourceDurationSeconds,
        HighlightAnalysisOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        options ??= new HighlightAnalysisOptions();
        var active = transcript.Segments
            .Where(segment => !segment.IsExcluded && !string.IsNullOrWhiteSpace(segment.Text))
            .OrderBy(segment => segment.StartSeconds)
            .ToList();
        if (active.Count == 0)
        {
            return [];
        }

        var windows = new List<HighlightCandidate>();
        for (var startIndex = 0; startIndex < active.Count; startIndex++)
        {
            var endIndex = startIndex;
            var start = active[startIndex].StartSeconds;
            var end = active[startIndex].EndSeconds;
            while (endIndex + 1 < active.Count &&
                   end - start < options.TargetDurationSeconds &&
                   active[endIndex + 1].EndSeconds - start <= options.MaximumDurationSeconds)
            {
                endIndex++;
                end = active[endIndex].EndSeconds;
            }

            var duration = end - start;
            if (duration < options.MinimumDurationSeconds)
            {
                continue;
            }

            var segments = active.GetRange(startIndex, endIndex - startIndex + 1);
            var text = string.Join(" ", segments.Select(segment => segment.Text.Trim()));
            var (score, reasons) = Score(text, segments, duration, options.TargetDurationSeconds);
            windows.Add(new HighlightCandidate
            {
                StartSeconds = Math.Max(0, start - options.ContextPaddingSeconds),
                EndSeconds = Math.Min(sourceDurationSeconds, end + options.ContextPaddingSeconds),
                Score = score,
                PreviewText = text.Length <= 260 ? text : text[..257] + "...",
                Reasons = reasons
            });
        }

        var selected = new List<HighlightCandidate>();
        foreach (var candidate in windows.OrderByDescending(item => item.Score))
        {
            if (selected.Any(existing => IntersectionOverUnion(existing, candidate) > 0.55))
            {
                continue;
            }

            selected.Add(candidate);
            if (selected.Count >= options.MaximumCandidates)
            {
                break;
            }
        }

        return selected
            .OrderByDescending(item => item.Score)
            .Select((item, index) => item with
            {
                Rank = index + 1,
                IsSelected = index < 3
            })
            .ToList();
    }

    private static (double Score, List<string> Reasons) Score(
        string text,
        IReadOnlyList<TranscriptSegment> segments,
        double duration,
        double targetDuration)
    {
        var reasons = new List<string>();
        var normalized = text.ToLowerInvariant();
        var score = 45d;

        var strongTerms = StrongThaiTerms.Count(term => normalized.Contains(term, StringComparison.OrdinalIgnoreCase)) +
                          StrongEnglishTerms.Count(term => normalized.Contains(term, StringComparison.OrdinalIgnoreCase));
        if (strongTerms > 0)
        {
            score += Math.Min(24, strongTerms * 6);
            reasons.Add($"มีคำสัญญาณเนื้อหาสำคัญ {strongTerms} จุด");
        }

        if (QuestionRegex().IsMatch(text))
        {
            score += 8;
            reasons.Add("มีคำถามหรือประโยคชวนติดตาม");
        }

        if (NumberRegex().IsMatch(text))
        {
            score += 6;
            reasons.Add("มีตัวเลขหรือรายการที่จับต้องได้");
        }

        var words = TokenRegex().Matches(text).Count;
        var density = duration <= 0 ? 0 : words / duration;
        if (density >= 1.5)
        {
            score += Math.Min(10, density * 2.2);
            reasons.Add("ความหนาแน่นของคำพูดดี");
        }

        var confidence = segments
            .Where(segment => segment.AverageProbability is not null)
            .Select(segment => segment.AverageProbability!.Value)
            .DefaultIfEmpty(0.75)
            .Average();
        score += Math.Clamp((confidence - 0.5) * 20, -5, 8);
        if (confidence >= 0.75)
        {
            reasons.Add("Transcript มีความมั่นใจค่อนข้างดี");
        }

        var durationPenalty = Math.Abs(duration - targetDuration) / Math.Max(1, targetDuration) * 12;
        score -= durationPenalty;
        if (duration is >= 18 and <= 50)
        {
            reasons.Add("ความยาวเหมาะกับคลิปสั้น");
        }

        if (reasons.Count == 0)
        {
            reasons.Add("ช่วงคำพูดต่อเนื่องและความยาวเหมาะสม");
        }

        return (Math.Clamp(score, 0, 100), reasons);
    }

    private static double IntersectionOverUnion(HighlightCandidate left, HighlightCandidate right)
    {
        var intersection = Math.Max(0, Math.Min(left.EndSeconds, right.EndSeconds) -
                                       Math.Max(left.StartSeconds, right.StartSeconds));
        var union = left.DurationSeconds + right.DurationSeconds - intersection;
        return union <= 0 ? 0 : intersection / union;
    }

    [GeneratedRegex(@"[?？]|\b(why|how|what|when|where|who)\b|ทำไม|อย่างไร|อะไร|เมื่อไร|ที่ไหน", RegexOptions.IgnoreCase)]
    private static partial Regex QuestionRegex();

    [GeneratedRegex(@"\d")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"[\p{L}\p{M}\p{N}]+")]
    private static partial Regex TokenRegex();
}
