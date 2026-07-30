using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AutoCutStudio.Core.Models;
using UglyToad.PdfPig;

namespace AutoCutStudio.Infrastructure;

public sealed partial class DocumentContentReader
{
    public async Task<IReadOnlyList<DocumentSection>> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("ไม่พบเอกสาร", path);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var paragraphs = extension switch
        {
            ".txt" or ".md" or ".markdown" => await ReadTextAsync(path, cancellationToken),
            ".docx" => ReadDocx(path),
            ".pdf" => ReadPdf(path),
            _ => throw new InvalidDataException("รองรับเอกสาร TXT, Markdown, DOCX และ PDF เท่านั้น")
        };
        return BuildSections(paragraphs, Path.GetFileNameWithoutExtension(path));
    }

    private static async Task<List<string>> ReadTextAsync(string path, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(path, cancellationToken);
        return SplitParagraphs(text);
    }

    private static List<string> ReadDocx(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry("word/document.xml")
                    ?? throw new InvalidDataException("DOCX ไม่มี word/document.xml");
        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        return document.Descendants(word + "p")
            .Select(paragraph => string.Concat(paragraph.Descendants(word + "t").Select(text => text.Value)).Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
    }

    private static List<string> ReadPdf(string path)
    {
        using var document = PdfDocument.Open(path);
        return document.GetPages()
            .SelectMany(page => SplitParagraphs(page.Text))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
    }

    internal static IReadOnlyList<DocumentSection> BuildSections(
        IReadOnlyList<string> paragraphs,
        string fallbackTitle)
    {
        var sections = new List<DocumentSection>();
        var currentHeading = string.IsNullOrWhiteSpace(fallbackTitle) ? "เอกสาร" : fallbackTitle.Trim();
        var body = new StringBuilder();

        void Flush()
        {
            var value = body.ToString().Trim();
            if (string.IsNullOrWhiteSpace(value)) return;
            foreach (var chunk in Chunk(value, 420))
            {
                sections.Add(new DocumentSection
                {
                    Index = sections.Count + 1,
                    Heading = currentHeading,
                    Body = chunk,
                    SuggestedDurationSeconds = Math.Clamp(3.5 + chunk.Length / 38d, 4, 12)
                });
            }
            body.Clear();
        }

        foreach (var paragraph in paragraphs)
        {
            var clean = Clean(paragraph);
            if (string.IsNullOrWhiteSpace(clean)) continue;
            if (TryGetHeading(clean, out var heading))
            {
                Flush();
                currentHeading = heading;
                continue;
            }
            if (body.Length > 0) body.AppendLine();
            body.Append(RemoveListPrefix(clean));
            if (body.Length >= 520) Flush();
        }
        Flush();

        if (sections.Count == 0 && paragraphs.Count > 0)
        {
            var fallbackBody = string.Join(Environment.NewLine, paragraphs.Select(Clean).Where(value => !string.IsNullOrWhiteSpace(value)));
            if (!string.IsNullOrWhiteSpace(fallbackBody))
            {
                foreach (var chunk in Chunk(fallbackBody, 420))
                {
                    sections.Add(new DocumentSection
                    {
                        Index = sections.Count + 1,
                        Heading = currentHeading,
                        Body = chunk,
                        SuggestedDurationSeconds = Math.Clamp(3.5 + chunk.Length / 38d, 4, 12)
                    });
                }
            }
        }

        if (sections.Count == 0)
            throw new InvalidDataException("เอกสารไม่มีข้อความที่นำไปสร้างวิดีโอได้");
        return sections;
    }

    private static List<string> SplitParagraphs(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split(['\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();

    private static bool TryGetHeading(string value, out string heading)
    {
        heading = string.Empty;
        if (value.StartsWith('#'))
        {
            heading = HeadingPrefixRegex().Replace(value, string.Empty).Trim();
            return !string.IsNullOrWhiteSpace(heading);
        }
        if (ExplicitHeadingRegex().IsMatch(value))
        {
            heading = ExplicitHeadingPrefixRegex().Replace(value, string.Empty).Trim().TrimEnd(':', '：');
            return !string.IsNullOrWhiteSpace(heading);
        }
        if (value.Length <= 80 && (value.EndsWith(':') || value.EndsWith('：')))
        {
            heading = value.TrimEnd(':', '：').Trim();
            return !string.IsNullOrWhiteSpace(heading);
        }
        return false;
    }

    private static string RemoveListPrefix(string value) =>
        ListPrefixRegex().Replace(value, string.Empty).Trim();

    private static string Clean(string value) =>
        WhitespaceRegex().Replace(value, " ").Trim();

    private static IEnumerable<string> Chunk(string value, int maximumLength)
    {
        var remaining = value.Trim();
        while (remaining.Length > maximumLength)
        {
            var split = remaining.LastIndexOfAny([' ', '。', '.', '!', '?', '！', '？'], maximumLength);
            if (split < maximumLength / 2) split = maximumLength;
            yield return remaining[..split].Trim();
            remaining = remaining[split..].Trim();
        }
        if (!string.IsNullOrWhiteSpace(remaining)) yield return remaining;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^#{1,6}\s*")]
    private static partial Regex HeadingPrefixRegex();

    [GeneratedRegex(@"^(หัวข้อ|บทที่|ส่วนที่|chapter|section)\s*[0-9๐-๙A-Za-z.-]*\s*[:：-]?\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitHeadingPrefixRegex();

    [GeneratedRegex(@"^(หัวข้อ|บทที่|ส่วนที่|chapter|section)(\s|[0-9๐-๙])", RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitHeadingRegex();

    [GeneratedRegex(@"^(?:[-*•]+|\d+[.)]|[๐-๙]+[.)])\s*")]
    private static partial Regex ListPrefixRegex();
}
