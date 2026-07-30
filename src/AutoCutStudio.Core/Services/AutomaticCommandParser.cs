using AutoCutStudio.Core.Models;

namespace AutoCutStudio.Core.Services;

public sealed class AutomaticCommandParser
{
    private static readonly string[] RemoveSilenceAliases =
    [
        "ตัดช่วงเงียบ", "ลบช่วงเงียบ", "ตัดความเงียบ", "ลบความเงียบ",
        "ตัดช่องว่าง", "ลบช่องว่าง", "remove silence", "cut silence", "remove silent"
    ];

    private static readonly string[] TranscribeAliases =
    [
        "ถอดเสียง", "แปลงเสียงเป็นข้อความ", "สร้าง transcript", "ทำ transcript",
        "speech to text", "transcribe"
    ];

    private static readonly string[] SubtitleAliases =
    [
        "สร้าง subtitle", "ทำ subtitle", "สร้างซับ", "ทำซับ", "ใส่ซับ",
        "สร้างคำบรรยาย", "export srt", "create subtitles", "subtitle"
    ];

    private static readonly string[] FillerAliases =
    [
        "ลบคำฟิลเลอร์", "ลบคำว่าเอ่อ", "ลบเอ่ออ่า", "ลบเอ่อ อ่า อืม",
        "remove filler", "remove filler words"
    ];

    public AutomaticEditRequest Parse(string? command)
    {
        var original = command?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(original))
        {
            return Unsupported(original, "กรุณาพิมพ์คำสั่ง เช่น ‘ตัดช่วงเงียบออก’ หรือ ‘ถอดเสียงภาษาไทย’");
        }

        var normalized = Normalize(original);
        if (Matches(normalized, RemoveSilenceAliases))
        {
            return Supported(
                original,
                AutomaticEditActions.RemoveSilence,
                "จะตรวจระดับเสียงด้วย FFmpeg แล้วสร้าง Timeline ที่ตัดช่วงเงียบออก");
        }

        if (Matches(normalized, FillerAliases))
        {
            return Supported(
                original,
                AutomaticEditActions.RemoveFillerWords,
                "จะเปิด Transcript เพื่อตรวจ Filler เดี่ยวและให้ผู้ใช้ยืนยันช่วงที่จะตัด");
        }

        if (Matches(normalized, SubtitleAliases))
        {
            return Supported(
                original,
                AutomaticEditActions.CreateSubtitles,
                "จะถอดเสียงหรือเปิด Transcript เดิม เพื่อแก้ไขและสร้าง SRT ภายในเครื่อง");
        }

        if (Matches(normalized, TranscribeAliases))
        {
            return Supported(
                original,
                AutomaticEditActions.TranscribeSpeech,
                "จะถอดเสียงด้วย Whisper Local และเปิด Transcript Editor");
        }

        return Unsupported(
            original,
            "คำสั่งนี้ยังไม่รองรับ ระบบปัจจุบันรองรับตัดช่วงเงียบ ถอดเสียง Subtitle และ Filler เดี่ยว");
    }

    private static bool Matches(string normalized, IEnumerable<string> aliases) =>
        aliases.Any(alias => normalized.Contains(Normalize(alias), StringComparison.Ordinal));

    private static AutomaticEditRequest Supported(string original, string action, string message) => new()
    {
        OriginalCommand = original,
        IsSupported = true,
        Action = action,
        Message = message
    };

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
