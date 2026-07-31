using System.Globalization;
using System.Text;
using AutoCutStudio.Core.Models;

namespace AutoCutStudio.Core.Services;

/// <summary>
/// Produces platform-safe caption pages without depending on WPF or an installed font.
/// It measures Unicode text elements heuristically, keeps Thai combining marks together,
/// limits captions to the configured line count, and splits long transcript segments
/// into multiple timed pages instead of allowing text to overflow the frame.
/// </summary>
public sealed class CaptionLayoutService
{
    public IReadOnlyList<CaptionPage> Layout(string? text, SocialExportPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        var normalized = Normalize(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return [];
        }

        var safeZone = preset.CaptionSafeZone ?? new CaptionSafeZone();
        var fontSize = Math.Max(safeZone.MinimumFontSize, safeZone.PreferredFontSize);
        var pages = BuildPages(normalized, preset.Width, safeZone, fontSize);

        while (pages.Count > 1 && fontSize > safeZone.MinimumFontSize)
        {
            var nextSize = Math.Max(safeZone.MinimumFontSize, fontSize - 2);
            if (nextSize == fontSize)
            {
                break;
            }

            var candidate = BuildPages(normalized, preset.Width, safeZone, nextSize);
            if (candidate.Count > pages.Count && nextSize <= safeZone.MinimumFontSize)
            {
                break;
            }

            pages = candidate;
            fontSize = nextSize;
            if (pages.Count == 1)
            {
                break;
            }
        }

        return pages.Select(page => page with { FontSize = fontSize }).ToList();
    }

    private static List<CaptionPage> BuildPages(
        string text,
        int outputWidth,
        CaptionSafeZone safeZone,
        int fontSize)
    {
        var ratioWidth = Math.Max(1, outputWidth * Math.Clamp(safeZone.MaximumWidthRatio, 0.35, 0.95));
        var marginWidth = Math.Max(1, outputWidth - safeZone.MarginLeft - safeZone.MarginRight);
        var safeWidth = Math.Min(ratioWidth, marginWidth);
        var maximumUnits = Math.Max(4.5, safeWidth / Math.Max(1, fontSize));
        var maximumLines = Math.Clamp(safeZone.MaximumLines, 1, 3);
        var tokens = Tokenize(text);

        var pages = new List<CaptionPage>();
        var currentLines = new List<string>();
        var currentLine = new StringBuilder();
        var currentUnits = 0d;

        foreach (var token in tokens)
        {
            var tokenUnits = Measure(token);
            var separator = NeedsSpace(currentLine, token) ? " " : string.Empty;
            var candidateUnits = currentUnits + Measure(separator) + tokenUnits;

            if (currentLine.Length > 0 && candidateUnits > maximumUnits)
            {
                currentLines.Add(currentLine.ToString().Trim());
                currentLine.Clear();
                currentUnits = 0;

                if (currentLines.Count >= maximumLines)
                {
                    pages.Add(new CaptionPage { Lines = currentLines.ToList(), FontSize = fontSize });
                    currentLines.Clear();
                }
            }

            if (tokenUnits > maximumUnits)
            {
                foreach (var fragment in SplitOversizeToken(token, maximumUnits))
                {
                    if (currentLine.Length > 0)
                    {
                        currentLines.Add(currentLine.ToString().Trim());
                        currentLine.Clear();
                        currentUnits = 0;
                    }

                    currentLines.Add(fragment);
                    if (currentLines.Count >= maximumLines)
                    {
                        pages.Add(new CaptionPage { Lines = currentLines.ToList(), FontSize = fontSize });
                        currentLines.Clear();
                    }
                }
                continue;
            }

            if (NeedsSpace(currentLine, token))
            {
                currentLine.Append(' ');
                currentUnits += Measure(" ");
            }

            currentLine.Append(token);
            currentUnits += tokenUnits;
        }

        if (currentLine.Length > 0)
        {
            currentLines.Add(currentLine.ToString().Trim());
        }

        if (currentLines.Count > 0)
        {
            pages.Add(new CaptionPage { Lines = currentLines.ToList(), FontSize = fontSize });
        }

        return pages.Where(page => page.Lines.Any(line => !string.IsNullOrWhiteSpace(line))).ToList();
    }

    private static IReadOnlyList<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var buffer = new StringBuilder();
        var enumerator = StringInfo.GetTextElementEnumerator(text);

        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            if (string.IsNullOrWhiteSpace(element))
            {
                Flush(buffer, tokens);
                continue;
            }

            buffer.Append(element);
            if (IsNaturalBreak(element))
            {
                Flush(buffer, tokens);
            }
        }

        Flush(buffer, tokens);
        return tokens;
    }

    private static IEnumerable<string> SplitOversizeToken(string token, double maximumUnits)
    {
        var fragments = new List<string>();
        var current = new StringBuilder();
        var units = 0d;
        var enumerator = StringInfo.GetTextElementEnumerator(token);

        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            var elementUnits = Measure(element);
            if (current.Length > 0 && units + elementUnits > maximumUnits)
            {
                fragments.Add(current.ToString());
                current.Clear();
                units = 0;
            }

            current.Append(element);
            units += elementUnits;
        }

        if (current.Length > 0)
        {
            fragments.Add(current.ToString());
        }

        return fragments;
    }

    private static double Measure(string value)
    {
        var units = 0d;
        var enumerator = StringInfo.GetTextElementEnumerator(value);
        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            var rune = element.EnumerateRunes().FirstOrDefault();
            if (string.IsNullOrWhiteSpace(element))
            {
                units += 0.32;
            }
            else if (rune.Value is >= 0x0E00 and <= 0x0E7F)
            {
                units += 0.92;
            }
            else if (char.IsPunctuation(element, 0))
            {
                units += 0.48;
            }
            else if (rune.Value >= 0x2E80)
            {
                units += 1.0;
            }
            else if (char.IsUpper(element, 0))
            {
                units += 0.72;
            }
            else
            {
                units += 0.58;
            }
        }

        return units;
    }

    private static bool NeedsSpace(StringBuilder current, string token)
    {
        if (current.Length == 0 || token.Length == 0)
        {
            return false;
        }

        var previous = current[^1];
        var next = token[0];
        return IsLatinOrDigit(previous) && IsLatinOrDigit(next);
    }

    private static bool IsLatinOrDigit(char value) =>
        char.IsDigit(value) || value is >= 'A' and <= 'Z' || value is >= 'a' and <= 'z';

    private static bool IsNaturalBreak(string element) => element is
        "." or "," or ";" or ":" or "!" or "?" or "…" or "ฯ" or "ๆ" or "–" or "—" or "/";

    private static string Normalize(string? value) => string.Join(
        ' ',
        (value ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries));

    private static void Flush(StringBuilder buffer, ICollection<string> tokens)
    {
        if (buffer.Length == 0)
        {
            return;
        }

        tokens.Add(buffer.ToString());
        buffer.Clear();
    }
}
