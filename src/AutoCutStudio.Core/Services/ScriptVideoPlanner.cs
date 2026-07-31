using System.Text;
using System.Text.RegularExpressions;
using AutoCutStudio.Core.Models;

namespace AutoCutStudio.Core.Services;

public sealed record ScriptVideoPlanningOptions
{
    public int MaximumSceneCharacters { get; init; } = 220;
    public double MinimumSceneDurationSeconds { get; init; } = 3.0;
    public double MaximumSceneDurationSeconds { get; init; } = 18.0;
    public double NarrationCharactersPerSecond { get; init; } = 12.5;
    public bool UseSequentialAssetFallback { get; init; } = true;
}

public sealed record ScriptVideoScene
{
    public int Index { get; init; }
    public string Heading { get; init; } = string.Empty;
    public string Narration { get; init; } = string.Empty;
    public string VisualKeyword { get; init; } = string.Empty;
    public double DurationSeconds { get; init; }
    public string? VisualAssetPath { get; init; }
    public string VisualSource { get; init; } = "generated_card";

    public string AssetDisplayName => string.IsNullOrWhiteSpace(VisualAssetPath)
        ? "ใช้ Motion Text / Background"
        : Path.GetFileName(VisualAssetPath);
}

public sealed record ScriptVideoPlan
{
    public string Title { get; init; } = "Script Video";
    public string OriginalScript { get; init; } = string.Empty;
    public List<ScriptVideoScene> Scenes { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public double EstimatedDurationSeconds => Scenes.Sum(scene => scene.DurationSeconds);

    public List<DocumentSection> ToDocumentSections() => Scenes.Select(scene => new DocumentSection
    {
        Index = scene.Index,
        Heading = scene.Heading,
        Body = scene.Narration,
        SuggestedDurationSeconds = scene.DurationSeconds
    }).ToList();

    public IReadOnlyDictionary<int, string> SceneAssets() => Scenes
        .Where(scene => !string.IsNullOrWhiteSpace(scene.VisualAssetPath))
        .ToDictionary(scene => scene.Index, scene => scene.VisualAssetPath!, EqualityComparer<int>.Default);
}

public sealed class ScriptVideoPlanner
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "และ", "หรือ", "คือ", "เป็น", "ที่", "ใน", "ของ", "ให้", "ได้", "มี", "จาก", "กับ", "เพื่อ", "โดย",
        "the", "and", "or", "is", "are", "to", "of", "in", "for", "with", "from", "this", "that"
    };

    private static readonly HashSet<string> SupportedAssetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".mp4", ".mov", ".mkv", ".webm"
    };

    public ScriptVideoPlan Plan(
        string script,
        string? assetsDirectory,
        ScriptVideoPlanningOptions? options = null,
        string? title = null)
    {
        options ??= new ScriptVideoPlanningOptions();
        if (string.IsNullOrWhiteSpace(script))
            throw new ArgumentException("กรุณาใส่สคริปต์ก่อนสร้างวิดีโอ", nameof(script));

        var normalizedScript = NormalizeWhitespace(script);
        var chunks = SplitScript(normalizedScript, Math.Clamp(options.MaximumSceneCharacters, 80, 600));
        if (chunks.Count == 0)
            throw new InvalidDataException("ไม่สามารถแบ่งสคริปต์เป็นฉากได้");

        var assets = EnumerateAssets(assetsDirectory);
        var unusedAssets = new Queue<string>(assets);
        var usedAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scenes = new List<ScriptVideoScene>(chunks.Count);

        for (var index = 0; index < chunks.Count; index++)
        {
            var narration = chunks[index].Trim();
            var heading = BuildHeading(narration, index + 1);
            var keywords = ExtractKeywords(heading + " " + narration);
            var matched = MatchAsset(narration, heading, keywords, assets, usedAssets);
            var source = "keyword_match";

            if (matched is null && options.UseSequentialAssetFallback)
            {
                while (unusedAssets.Count > 0)
                {
                    var candidate = unusedAssets.Dequeue();
                    if (usedAssets.Add(candidate))
                    {
                        matched = candidate;
                        source = "sequence_fallback";
                        break;
                    }
                }
            }
            else if (matched is not null)
            {
                usedAssets.Add(matched);
            }

            var estimated = narration.Length / Math.Max(4, options.NarrationCharactersPerSecond) + 1.0;
            var duration = Math.Clamp(
                estimated,
                Math.Max(2, options.MinimumSceneDurationSeconds),
                Math.Max(options.MinimumSceneDurationSeconds, options.MaximumSceneDurationSeconds));

            scenes.Add(new ScriptVideoScene
            {
                Index = index,
                Heading = heading,
                Narration = narration,
                VisualKeyword = keywords.FirstOrDefault() ?? heading,
                DurationSeconds = Math.Round(duration, 2),
                VisualAssetPath = matched,
                VisualSource = matched is null ? "generated_card" : source
            });
        }

        return new ScriptVideoPlan
        {
            Title = string.IsNullOrWhiteSpace(title) ? BuildHeading(chunks[0], 1) : title.Trim(),
            OriginalScript = normalizedScript,
            Scenes = scenes
        };
    }

    public string ApplyPronunciationDictionary(string text, string? dictionaryText)
    {
        if (string.IsNullOrWhiteSpace(dictionaryText))
            return text;

        var output = text;
        foreach (var rawLine in dictionaryText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var separator = line.Contains("->", StringComparison.Ordinal) ? "->" : "=";
            var parts = line.Split(separator, 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]))
                continue;

            output = output.Replace(parts[0], parts[1], StringComparison.OrdinalIgnoreCase);
        }
        return output;
    }

    private static List<string> SplitScript(string script, int maximumCharacters)
    {
        var paragraphs = Regex.Split(script, @"\n\s*\n")
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .ToList();
        if (paragraphs.Count == 0)
            paragraphs.Add(script.Trim());

        var output = new List<string>();
        foreach (var paragraph in paragraphs)
        {
            if (paragraph.Length <= maximumCharacters)
            {
                output.Add(paragraph);
                continue;
            }

            var sentences = Regex.Split(paragraph, @"(?<=[\.\!\?。！？])\s+|\n+")
                .Select(item => item.Trim())
                .Where(item => item.Length > 0)
                .ToList();
            if (sentences.Count <= 1)
            {
                output.AddRange(SplitByLength(paragraph, maximumCharacters));
                continue;
            }

            var buffer = new StringBuilder();
            foreach (var sentence in sentences)
            {
                if (sentence.Length > maximumCharacters)
                {
                    if (buffer.Length > 0)
                    {
                        output.Add(buffer.ToString().Trim());
                        buffer.Clear();
                    }
                    output.AddRange(SplitByLength(sentence, maximumCharacters));
                    continue;
                }

                if (buffer.Length > 0 && buffer.Length + 1 + sentence.Length > maximumCharacters)
                {
                    output.Add(buffer.ToString().Trim());
                    buffer.Clear();
                }
                if (buffer.Length > 0)
                    buffer.Append(' ');
                buffer.Append(sentence);
            }
            if (buffer.Length > 0)
                output.Add(buffer.ToString().Trim());
        }
        return output;
    }

    private static IEnumerable<string> SplitByLength(string value, int maximumCharacters)
    {
        var remaining = value.Trim();
        while (remaining.Length > maximumCharacters)
        {
            var split = remaining.LastIndexOf(' ', maximumCharacters);
            if (split < maximumCharacters / 2)
                split = maximumCharacters;
            yield return remaining[..split].Trim();
            remaining = remaining[split..].Trim();
        }
        if (remaining.Length > 0)
            yield return remaining;
    }

    private static string BuildHeading(string narration, int index)
    {
        var firstLine = narration.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim()
                        ?? narration.Trim();
        var punctuation = firstLine.IndexOfAny(['.', '!', '?', '。', '！', '？', ':', '：']);
        if (punctuation is > 5 and < 64)
            firstLine = firstLine[..punctuation];
        if (firstLine.Length > 52)
            firstLine = firstLine[..52].TrimEnd() + "…";
        return string.IsNullOrWhiteSpace(firstLine) ? $"ฉากที่ {index}" : firstLine;
    }

    private static List<string> EnumerateAssets(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return [];
        return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => SupportedAssetExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? MatchAsset(
        string narration,
        string heading,
        IReadOnlyList<string> keywords,
        IReadOnlyList<string> assets,
        IReadOnlySet<string> used)
    {
        var scene = NormalizeForMatch(heading + narration);
        var scored = assets
            .Where(path => !used.Contains(path))
            .Select(path =>
            {
                var stem = NormalizeForMatch(Path.GetFileNameWithoutExtension(path));
                var score = 0;
                if (stem.Length >= 3 && scene.Contains(stem, StringComparison.OrdinalIgnoreCase))
                    score += 12;
                foreach (var keyword in keywords)
                {
                    var normalized = NormalizeForMatch(keyword);
                    if (normalized.Length < 2)
                        continue;
                    if (stem.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                        score += 5;
                    else if (normalized.Contains(stem, StringComparison.OrdinalIgnoreCase) && stem.Length >= 3)
                        score += 3;
                }
                return (Path: path, Score: score);
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return scored.Score > 0 ? scored.Path : null;
    }

    private static IReadOnlyList<string> ExtractKeywords(string value)
    {
        var tokens = Regex.Matches(value, @"[A-Za-z0-9][A-Za-z0-9\-_]{1,}|[ก-๙]{2,}")
            .Select(match => match.Value.Trim())
            .Where(token => token.Length >= 2 && !StopWords.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(token => token.Length)
            .Take(12)
            .ToList();
        return tokens;
    }

    private static string NormalizeWhitespace(string value)
    {
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        normalized = Regex.Replace(normalized, @"[\t ]+", " ");
        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");
        return normalized.Trim();
    }

    private static string NormalizeForMatch(string value) => Regex.Replace(
        value.Normalize(NormalizationForm.FormC).ToLowerInvariant(),
        @"[^a-z0-9ก-๙]+",
        string.Empty);
}
