using AutoCutStudio.Core.Models;

namespace AutoCutStudio.Core.Services;

public sealed class AutomaticCommandParser
{
    private static readonly string[] RemoveSilenceAliases =
    [
        "ตัดช่วงเงียบ",
        "ลบช่วงเงียบ",
        "ตัดความเงียบ",
        "ลบความเงียบ",
        "ตัดช่องว่าง",
        "ลบช่องว่าง",
        "remove silence",
        "cut silence",
        "remove silent"
    ];

    public AutomaticEditRequest Parse(string? command)
    {
        var original = command?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(original))
        {
            return Unsupported(original, "กรุณาพิมพ์คำสั่ง เช่น ‘ตัดช่วงเงียบออก’");
        }

        var normalized = Normalize(original);
        if (RemoveSilenceAliases.Any(alias => normalized.Contains(Normalize(alias), StringComparison.Ordinal)))
        {
            return new AutomaticEditRequest
            {
                OriginalCommand = original,
                IsSupported = true,
                Action = AutomaticEditActions.RemoveSilence,
                Message = "จะตรวจระดับเสียงด้วย FFmpeg แล้วสร้าง Timeline ที่ตัดช่วงเงียบออก"
            };
        }

        return Unsupported(
            original,
            "Build นี้รองรับคำสั่งตัดช่วงเงียบเท่านั้น ส่วนคำพูดผิด Subtitle และ Highlight ยังไม่รองรับ");
    }

    private static AutomaticEditRequest Unsupported(string original, string message) => new()
    {
        OriginalCommand = original,
        IsSupported = false,
        Message = message
    };

    private static string Normalize(string value) =>
        string.Join(' ', value.Trim().ToLowerInvariant().Split(
            [' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries));
}
