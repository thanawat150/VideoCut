using System.Globalization;
using System.Text;
using AutoCutStudio.Core.Models;

namespace AutoCutStudio.Core.Services;

public sealed class AssCaptionBuilder
{
    public string Build(
        TranscriptDocument transcript,
        HighlightCandidate candidate,
        SocialExportPreset preset,
        string? hookText,
        string? ctaText)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[Script Info]");
        builder.AppendLine("ScriptType: v4.00+");
        builder.AppendLine($"PlayResX: {preset.Width}");
        builder.AppendLine($"PlayResY: {preset.Height}");
        builder.AppendLine("ScaledBorderAndShadow: yes");
        builder.AppendLine("WrapStyle: 2");
        builder.AppendLine();
        builder.AppendLine("[V4+ Styles]");
        builder.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        var captionSize = preset.Height > preset.Width ? 72 : 54;
        var hookSize = preset.Height > preset.Width ? 80 : 64;
        builder.AppendLine($"Style: Caption,Arial,{captionSize},&H00FFFFFF,&H0000FFFF,&H00101010,&H88000000,-1,0,0,0,100,100,0,0,1,5,2,2,90,90,190,1");
        builder.AppendLine($"Style: Hook,Arial,{hookSize},&H0000FFFF,&H00FFFFFF,&H00101010,&H88000000,-1,0,0,0,100,100,0,0,1,6,2,8,80,80,160,1");
        builder.AppendLine($"Style: CTA,Arial,{captionSize},&H00FFFFFF,&H0000FFFF,&H00101010,&HAA000000,-1,0,0,0,100,100,0,0,3,2,0,2,80,80,90,1");
        builder.AppendLine();
        builder.AppendLine("[Events]");
        builder.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

        if (!string.IsNullOrWhiteSpace(hookText))
        {
            var hookEnd = Math.Min(candidate.DurationSeconds, 3.2);
            builder.AppendLine(BuildDialogue(
                2,
                0,
                hookEnd,
                "Hook",
                $"{{\\fad(120,180)\\t(0,220,\\fscx108\\fscy108)}}{Escape(hookText)}"));
        }

        foreach (var segment in transcript.Segments.Where(segment =>
                     !segment.IsExcluded &&
                     segment.EndSeconds > candidate.StartSeconds &&
                     segment.StartSeconds < candidate.EndSeconds))
        {
            var start = Math.Max(0, segment.StartSeconds - candidate.StartSeconds);
            var end = Math.Min(candidate.DurationSeconds, segment.EndSeconds - candidate.StartSeconds);
            if (end <= start + 0.05 || string.IsNullOrWhiteSpace(segment.Text))
            {
                continue;
            }

            var text = Wrap(Escape(segment.Text.Trim()), preset.Height > preset.Width ? 24 : 36);
            builder.AppendLine(BuildDialogue(
                1,
                start,
                end,
                "Caption",
                $"{{\\fad(70,70)\\t(0,120,\\fscx104\\fscy104)}}{text}"));
        }

        if (!string.IsNullOrWhiteSpace(ctaText))
        {
            var start = Math.Max(0, candidate.DurationSeconds - 3.2);
            builder.AppendLine(BuildDialogue(
                3,
                start,
                candidate.DurationSeconds,
                "CTA",
                $"{{\\fad(180,120)}}{Escape(ctaText)}"));
        }

        return builder.ToString();
    }

    private static string BuildDialogue(
        int layer,
        double start,
        double end,
        string style,
        string text) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Dialogue: {layer},{FormatTime(start)},{FormatTime(end)},{style},,0,0,0,,{text}");

    private static string FormatTime(double seconds)
    {
        var value = TimeSpan.FromSeconds(Math.Max(0, seconds));
        var hours = (int)value.TotalHours;
        var centiseconds = value.Milliseconds / 10;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{hours}:{value.Minutes:00}:{value.Seconds:00}.{centiseconds:00}");
    }

    private static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("{", "\\{", StringComparison.Ordinal)
        .Replace("}", "\\}", StringComparison.Ordinal)
        .Replace(Environment.NewLine, "\\N", StringComparison.Ordinal)
        .Replace("\n", "\\N", StringComparison.Ordinal);

    private static string Wrap(string text, int maximumCharacters)
    {
        if (text.Length <= maximumCharacters)
        {
            return text;
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 1)
        {
            var midpoint = Math.Clamp(text.Length / 2, 1, text.Length - 1);
            return text[..midpoint] + "\\N" + text[midpoint..];
        }

        var lines = new List<string>();
        var current = new StringBuilder();
        foreach (var word in words)
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > maximumCharacters)
            {
                lines.Add(current.ToString());
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append(' ');
            }

            current.Append(word);
        }

        if (current.Length > 0)
        {
            lines.Add(current.ToString());
        }

        return string.Join("\\N", lines.Take(3));
    }
}
