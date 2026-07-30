using System.Text.RegularExpressions;
using AutoCutStudio.Core.Models;

namespace AutoCutStudio.Core.Services;

public sealed partial class BrollSuggestionService
{
    private static readonly Dictionary<string, string> ThaiShotMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ป่า"] = "ภาพกว้างป่า ต้นไม้ หรือพื้นที่สีเขียว",
        ["ป่าชายเลน"] = "ภาพโดรนป่าชายเลน รากโกงกาง และแนวชายฝั่ง",
        ["โดรน"] = "ภาพโดรนกำลังบินหรือภาพมุมสูงของพื้นที่",
        ["แผนที่"] = "ภาพหน้าจอ GIS แผนที่ หรือการซูมเข้าพื้นที่",
        ["ข้อมูล"] = "ภาพ Dashboard ตาราง หรือกราฟที่สัมพันธ์กับประเด็น",
        ["คาร์บอน"] = "ภาพต้นไม้ การวัดแปลง และกราฟคาร์บอนเครดิต",
        ["ชุมชน"] = "ภาพกิจกรรมชุมชน การประชุม หรือการทำงานภาคสนาม",
        ["สำรวจ"] = "ภาพทีมลงพื้นที่ GPS แบบฟอร์ม และการถ่ายภาพจุดสำรวจ"
    };

    private static readonly Dictionary<string, string> EnglishShotMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["forest"] = "Wide forest, tree canopy or green landscape footage",
        ["mangrove"] = "Mangrove roots, coast and drone establishing shot",
        ["drone"] = "Drone launch or aerial establishing footage",
        ["map"] = "GIS map, spatial overlay or location zoom",
        ["data"] = "Dashboard, table or chart related to the point",
        ["carbon"] = "Tree measurement and carbon chart footage",
        ["community"] = "Community meeting or field activity footage",
        ["survey"] = "Field survey, GPS and data collection footage"
    };

    public IReadOnlyList<BrollSuggestion> Suggest(
        TranscriptDocument transcript,
        ObjectAnalysisResult? objectAnalysis,
        string localAssetDirectory,
        int maximumSuggestions = 20)
    {
        var candidates = new List<Candidate>();
        foreach (var segment in transcript.Segments.Where(item => !item.IsExcluded))
        {
            var text = segment.Text.Trim();
            foreach (var pair in ThaiShotMap.Concat(EnglishShotMap))
            {
                if (text.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
                    candidates.Add(new Candidate(pair.Key, pair.Value, segment.StartSeconds, segment.EndSeconds, "พบคำสำคัญใน Transcript"));
            }

            foreach (Match match in EnglishWordRegex().Matches(text.ToLowerInvariant()))
            {
                if (match.Value.Length >= 5)
                    candidates.Add(new Candidate(match.Value, $"B-roll ที่สื่อถึง ‘{match.Value}’", segment.StartSeconds, segment.EndSeconds, "คำเนื้อหาที่เกิดใน Transcript"));
            }
        }

        if (objectAnalysis is not null)
        {
            foreach (var detection in objectAnalysis.Detections
                         .GroupBy(item => new { item.Label, Bucket = (int)(item.TimeSeconds / 5) })
                         .Select(group => group.OrderByDescending(item => item.Confidence).First()))
            {
                candidates.Add(new Candidate(
                    detection.Label,
                    $"ภาพเสริมที่เชื่อมกับวัตถุ ‘{detection.Label}’",
                    Math.Max(0, detection.TimeSeconds - 1.5),
                    detection.TimeSeconds + 2.5,
                    $"YOLO ตรวจพบ {detection.Label} ความมั่นใจ {detection.Confidence:P0}"));
            }
        }

        var assets = Directory.Exists(localAssetDirectory)
            ? Directory.EnumerateFiles(localAssetDirectory, "*", SearchOption.AllDirectories)
                .Where(path => new[] { ".mp4", ".mov", ".mkv", ".jpg", ".jpeg", ".png", ".webp" }
                    .Contains(Path.GetExtension(path).ToLowerInvariant()))
                .ToList()
            : [];

        return candidates
            .GroupBy(item => new { Keyword = item.Keyword.ToLowerInvariant(), Bucket = (int)(item.Start / 5) })
            .Select(group => group.First())
            .Take(Math.Clamp(maximumSuggestions, 1, 100))
            .Select((candidate, index) => new BrollSuggestion
            {
                Rank = index + 1,
                Keyword = candidate.Keyword,
                SuggestedShot = candidate.Shot,
                StartSeconds = candidate.Start,
                EndSeconds = candidate.End,
                MatchedLocalAsset = FindLocalAsset(assets, candidate.Keyword),
                Reasons = [candidate.Reason]
            })
            .ToList();
    }

    private static string? FindLocalAsset(IReadOnlyList<string> assets, string keyword)
    {
        var normalized = Normalize(keyword);
        return assets.FirstOrDefault(path =>
            Normalize(Path.GetFileNameWithoutExtension(path)).Contains(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string value) =>
        NonWordRegex().Replace(value.ToLowerInvariant(), string.Empty);

    private sealed record Candidate(string Keyword, string Shot, double Start, double End, string Reason);

    [GeneratedRegex(@"[a-z][a-z0-9-]+", RegexOptions.IgnoreCase)]
    private static partial Regex EnglishWordRegex();

    [GeneratedRegex(@"[^\p{L}\p{M}\p{N}]+")]
    private static partial Regex NonWordRegex();
}
