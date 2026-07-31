using System.Globalization;
using System.Text;
using AutoCutStudio.Core.Models;

namespace AutoCutStudio.Core.Services;

public sealed class AssCaptionBuilder
{
    private readonly CaptionLayoutService _layoutService;

    public AssCaptionBuilder(CaptionLayoutService? layoutService = null)
    {
        _layoutService = layoutService ?? new CaptionLayoutService();
    }

    public string Build(
        TranscriptDocument transcript,
        HighlightCandidate candidate,
        SocialExportPreset preset,
        string? hookText,
        string? ctaText)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(preset);

        var safe = preset.CaptionSafeZone ?? new CaptionSafeZone();
        var captionSize = Math.Clamp(safe.PreferredFontSize, safe.MinimumFontSize, 120);
        var hookSize = Math.Clamp(captionSize + (preset.Height > preset.Width ? 10 : 6), captionSize, 132);
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
        builder.AppendLine($"Style: Caption,Segoe UI,{captionSize},&H00FFFFFF,&H0000FFFF,&H00101010,&H88000000,-1,0,0,0,100,100,0,0,1,5,2,2,{safe.MarginLeft},{safe.MarginRight},{safe.MarginBottom},1");
        builder.AppendLine($"Style: Hook,Segoe UI,{hookSize},&H0000FFFF,&H00FFFFFF,&H00101010,&H88000000,-1,0,0,0,100,100,0,0,1,6,2,8,{safe.MarginLeft},{safe.MarginRight},{safe.MarginTop},1");
        builder.AppendLine($"Style: CTA,Segoe UI,{captionSize},&H00FFFFFF,&H0000FFFF,&H00101010,&HAA000000,-1,0,0,0,100,100,0,0,3,2,0,2,{safe.MarginLeft},{safe.MarginRight},{Math.Max(70, safe.MarginBottom - 70)},1");
        builder.AppendLine();
        builder.AppendLine("[Events]");
        builder.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

        if (preset.EnableHook && !string.IsNullOrWhiteSpace(hookText))
        {
            var hookEnd = Math.Min(candidate.DurationSeconds, 3.2);
            var hookPages = _layoutService.Layout(hookText, preset);
            var hook = hookPages.FirstOrDefault();
            if (hook is not null)
            {
                builder.AppendLine(BuildDialogue(
                    2,
                    0,
                    hookEnd,
                    "Hook",
                    $"{{\\fad(120,180)\\fs{Math.Min(hookSize, hook.FontSize + 8)}\\t(0,220,\\fscx106\\fscy106)}}{EscapeLines(hook.Lines)}"));
            }
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

            var pages = _layoutService.Layout(segment.Text.Trim(), preset);
            if (pages.Count == 0)
            {
                continue;
            }

            var pageDuration = Math.Max(0.08, (end - start) / pages.Count);
            for (var index = 0; index < pages.Count; index++)
            {
                var page = pages[index];
                var pageStart = start + pageDuration * index;
                var pageEnd = index == pages.Count - 1 ? end : Math.Min(end, pageStart + pageDuration);
                if (pageEnd <= pageStart + 0.04)
                {
                    continue;
                }

                builder.AppendLine(BuildDialogue(
                    1,
                    pageStart,
                    pageEnd,
                    "Caption",
                    $"{{\\fad(70,70)\\fs{page.FontSize}\\t(0,120,\\fscx102\\fscy102)}}{EscapeLines(page.Lines)}"));
            }
        }

        if (preset.EnableCta && !string.IsNullOrWhiteSpace(ctaText))
        {
            var start = Math.Max(0, candidate.DurationSeconds - 3.2);
            var ctaPages = _layoutService.Layout(ctaText, preset);
            var cta = ctaPages.FirstOrDefault();
            if (cta is not null)
            {
                builder.AppendLine(BuildDialogue(
                    3,
                    start,
                    candidate.DurationSeconds,
                    "CTA",
                    $"{{\\fad(180,120)\\fs{cta.FontSize}}}{EscapeLines(cta.Lines)}"));
            }
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

    private static string EscapeLines(IEnumerable<string> lines) => string.Join(
        "\\N",
        lines.Select(EscapeLine).Where(line => !string.IsNullOrWhiteSpace(line)));

    private static string EscapeLine(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("{", "\\{", StringComparison.Ordinal)
        .Replace("}", "\\}", StringComparison.Ordinal)
        .Replace("\r", " ", StringComparison.Ordinal)
        .Replace("\n", " ", StringComparison.Ordinal)
        .Trim();
}
