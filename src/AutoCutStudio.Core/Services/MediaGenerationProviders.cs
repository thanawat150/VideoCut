using System.Globalization;
using System.Text;

namespace AutoCutStudio.Core.Services;

public sealed record ProviderAvailability
{
    public string ProviderId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsReady { get; init; }
    public string Status { get; init; } = "unavailable";
    public string Message { get; init; } = string.Empty;
}

public sealed record SpeechSynthesisRequest
{
    public string Text { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
    public string Language { get; init; } = "auto";
    public string Voice { get; init; } = "auto";
    public string Model { get; init; } = "gpt-4o-mini-tts";
    public string Instructions { get; init; } = string.Empty;
    public double Speed { get; init; } = 1.0;
    public int Rate { get; init; }
    public int Volume { get; init; } = 100;
}

public sealed record ImageGenerationRequest
{
    public string Prompt { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
    public int Width { get; init; } = 1024;
    public int Height { get; init; } = 1024;
    public string Model { get; init; } = "gpt-image-1";
    public string Quality { get; init; } = "medium";
}

public sealed record VideoGenerationRequest
{
    public string Prompt { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
    public string? ReferenceImagePath { get; init; }
    public int Width { get; init; } = 720;
    public int Height { get; init; } = 1280;
    public int DurationSeconds { get; init; } = 4;
    public string Model { get; init; } = "sora-2";
}

public sealed record GeneratedMediaAsset
{
    public string ProviderId { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string Prompt { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public interface ISpeechSynthesisProvider
{
    string ProviderId { get; }
    string DisplayName { get; }
    Task<ProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default);
    Task<GeneratedMediaAsset> GenerateAsync(
        SpeechSynthesisRequest request,
        CancellationToken cancellationToken = default);
}

public interface IImageGenerationProvider
{
    string ProviderId { get; }
    string DisplayName { get; }
    Task<ProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default);
    Task<GeneratedMediaAsset> GenerateAsync(
        ImageGenerationRequest request,
        CancellationToken cancellationToken = default);
}

public interface IVideoGenerationProvider
{
    string ProviderId { get; }
    string DisplayName { get; }
    Task<ProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default);
    Task<GeneratedMediaAsset> GenerateAsync(
        VideoGenerationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class LanguageDetectionService
{
    public string Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "en";

        var thai = 0;
        var kana = 0;
        var hangul = 0;
        var han = 0;
        var arabic = 0;
        var cyrillic = 0;
        var latin = 0;

        foreach (var rune in text.EnumerateRunes())
        {
            var value = rune.Value;
            if (value is >= 0x0E00 and <= 0x0E7F)
                thai++;
            else if (value is >= 0x3040 and <= 0x30FF)
                kana++;
            else if (value is >= 0x1100 and <= 0x11FF or >= 0xAC00 and <= 0xD7AF)
                hangul++;
            else if (value is >= 0x4E00 and <= 0x9FFF)
                han++;
            else if (value is >= 0x0600 and <= 0x06FF)
                arabic++;
            else if (value is >= 0x0400 and <= 0x04FF)
                cyrillic++;
            else if ((value is >= 'A' and <= 'Z') || (value is >= 'a' and <= 'z'))
                latin++;
        }

        if (thai > 0 && thai >= latin)
            return "th";
        if (kana > 0)
            return "ja";
        if (hangul > 0)
            return "ko";
        if (arabic > 0)
            return "ar";
        if (cyrillic > 0)
            return "ru";
        if (han > 0)
            return "zh";
        return "en";
    }

    public string Normalize(string? language, string? text = null)
    {
        var value = language?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(value) || value == "auto")
            return Detect(text);
        var separator = value.IndexOf('-');
        return separator > 0 ? value[..separator] : value;
    }

    public string DisplayName(string language) => Normalize(language) switch
    {
        "th" => "ภาษาไทย",
        "en" => "English",
        "ja" => "日本語",
        "zh" => "中文",
        "ko" => "한국어",
        "es" => "Español",
        "fr" => "Français",
        "de" => "Deutsch",
        "pt" => "Português",
        "it" => "Italiano",
        "ar" => "العربية",
        "ru" => "Русский",
        _ => language
    };
}

public sealed class ScenePromptBuilder
{
    public string Build(
        string heading,
        string narration,
        int width,
        int height,
        string style = "cinematic documentary")
    {
        var orientation = height > width ? "vertical 9:16 composition" : width > height ? "landscape 16:9 composition" : "square composition";
        var subject = Compact(string.IsNullOrWhiteSpace(heading) ? narration : heading, 180);
        var context = Compact(narration, 620);
        return $"Create a {style} visual for a narrated video scene. Main subject: {subject}. Story context: {context}. " +
               $"Use a {orientation}, clear focal subject, realistic lighting, visual storytelling, natural depth, no text, no captions, no logo, no watermark.";
    }

    public static string OpenAiImageSize(int width, int height) =>
        height > width ? "1024x1536" : width > height ? "1536x1024" : "1024x1024";

    public static string OpenAiVideoSize(int width, int height) =>
        height > width ? "720x1280" : "1280x720";

    private static string Compact(string value, int maximumLength)
    {
        var normalized = string.Join(' ', value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength].TrimEnd() + "…";
    }
}
