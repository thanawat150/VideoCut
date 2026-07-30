using System.Globalization;
using System.Text;
using AutoCutStudio.Core.Models;

namespace AutoCutStudio.Core.Services;

public sealed class TemplateVideoAssBuilder
{
    public string Build(TemplateVideoRecipe recipe)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[Script Info]");
        builder.AppendLine("ScriptType: v4.00+");
        builder.AppendLine($"PlayResX: {recipe.Width}");
        builder.AppendLine($"PlayResY: {recipe.Height}");
        builder.AppendLine("ScaledBorderAndShadow: yes");
        builder.AppendLine("WrapStyle: 2");
        builder.AppendLine();
        builder.AppendLine("[V4+ Styles]");
        builder.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        var landscape = recipe.Width >= recipe.Height;
        var titleSize = landscape ? 72 : 64;
        var bodySize = landscape ? 44 : 50;
        builder.AppendLine($"Style: Title,Arial,{titleSize},&H0000FFFF,&H00FFFFFF,&H00101010,&H66000000,-1,0,0,0,100,100,0,0,1,4,2,8,100,100,120,1");
        builder.AppendLine($"Style: Body,Arial,{bodySize},&H00FFFFFF,&H0000FFFF,&H00101010,&H66000000,0,0,0,0,100,100,0,0,1,3,1,7,120,120,260,1");
        builder.AppendLine($"Style: Footer,Arial,{Math.Max(28, bodySize - 12)},&H00D1D5DB,&H00FFFFFF,&H00101010,&H66000000,0,0,0,0,100,100,0,0,1,2,1,3,80,80,60,1");
        builder.AppendLine();
        builder.AppendLine("[Events]");
        builder.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

        double cursor = 0;
        for (var index = 0; index < recipe.Sections.Count; index++)
        {
            var section = recipe.Sections[index];
            var duration = Math.Clamp(section.SuggestedDurationSeconds, 2, 20);
            var end = cursor + duration;
            builder.AppendLine(Dialogue(2, cursor, end, "Title",
                $"{{\\fad(180,180)\\t(0,300,\\fscx104\\fscy104)}}{Escape(section.Heading)}"));
            builder.AppendLine(Dialogue(1, cursor + 0.25, end, "Body",
                $"{{\\fad(220,180)}}{Wrap(Escape(section.Body), recipe.Width >= recipe.Height ? 52 : 30)}"));
            builder.AppendLine(Dialogue(0, cursor, end, "Footer",
                $"{index + 1}/{recipe.Sections.Count}  •  {Escape(recipe.Title)}"));
            cursor = end;
        }
        return builder.ToString();
    }

    public double TotalDuration(TemplateVideoRecipe recipe) =>
        recipe.Sections.Sum(section => Math.Clamp(section.SuggestedDurationSeconds, 2, 20));

    private static string Dialogue(int layer, double start, double end, string style, string text) =>
        string.Create(CultureInfo.InvariantCulture,
            $"Dialogue: {layer},{Time(start)},{Time(end)},{style},,0,0,0,,{text}");

    private static string Time(double seconds)
    {
        var value = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return string.Create(CultureInfo.InvariantCulture,
            $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds / 10:00}");
    }

    private static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("{", "\\{", StringComparison.Ordinal)
        .Replace("}", "\\}", StringComparison.Ordinal)
        .Replace("\r\n", "\\N", StringComparison.Ordinal)
        .Replace("\n", "\\N", StringComparison.Ordinal);

    private static string Wrap(string value, int width)
    {
        if (value.Length <= width) return value;
        var lines = new List<string>();
        var remaining = value;
        while (remaining.Length > width && lines.Count < 6)
        {
            var split = remaining.LastIndexOf(' ', Math.Min(width, remaining.Length - 1));
            if (split < width / 2) split = Math.Min(width, remaining.Length);
            lines.Add(remaining[..split].Trim());
            remaining = remaining[split..].Trim();
        }
        if (!string.IsNullOrWhiteSpace(remaining) && lines.Count < 6) lines.Add(remaining);
        return string.Join("\\N", lines);
    }
}
