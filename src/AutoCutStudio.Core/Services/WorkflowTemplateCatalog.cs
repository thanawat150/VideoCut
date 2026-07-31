using AutoCutStudio.Core.Models;

namespace AutoCutStudio.Core.Services;

public sealed class WorkflowTemplateCatalog
{
    public IReadOnlyList<VisualWorkflowDocument> CreateDefaults(Guid projectId) =>
    [
        CreateTalkingHeadShorts(projectId),
        CreateMultiPlatformShorts(projectId),
        CreateEventRecap(projectId),
        CreateTranscriptOnly(projectId)
    ];

    public VisualWorkflowDocument CreateTalkingHeadShorts(Guid projectId)
    {
        var input = Node(VisualNodeTypes.MediaInput, "เลือกคลิปพูด", 70, 130);
        var transcribe = Node(VisualNodeTypes.Transcribe, "ถอดเสียงภาษาไทย", 310, 130,
            ("language", "th"), ("gpu", "true"));
        var filler = Node(VisualNodeTypes.RemoveFillers, "ลบคำฟิลเลอร์", 550, 70,
            ("mode", "isolated_only"));
        var silence = Node(VisualNodeTypes.RemoveSilence, "ตัดช่วงเงียบ", 550, 190,
            ("preset", "balanced"));
        var captions = Node(VisualNodeTypes.CreateCaptions, "สร้างซับ Safe Zone", 790, 70,
            ("style", "animated"));
        var highlights = Node(VisualNodeTypes.AnalyzeHighlights, "หา Highlight", 790, 190,
            ("count", "3"), ("duration_seconds", "45"));
        var platform = Node(VisualNodeTypes.PlatformStyle, "TikTok", 1030, 130,
            ("preset", "tiktok"));
        var shorts = Node(VisualNodeTypes.CreateShorts, "สร้าง Shorts 3 คลิป", 1270, 130,
            ("count", "3"));
        var export = Node(VisualNodeTypes.ExportVideo, "Export 9:16", 1510, 130,
            ("preset", "tiktok"));

        return Workflow(
            projectId,
            "คลิปพูดลง TikTok",
            "ถอดเสียง ตัดคำฟิลเลอร์และช่วงเงียบ สร้างซับที่ไม่ล้นขอบ หา Highlight และ Export Shorts",
            [input, transcribe, filler, silence, captions, highlights, platform, shorts, export],
            [
                Link(input, transcribe),
                Link(transcribe, filler),
                Link(transcribe, silence),
                Link(filler, captions),
                Link(silence, highlights),
                Link(captions, platform),
                Link(highlights, platform),
                Link(platform, shorts),
                Link(shorts, export)
            ]);
    }

    public VisualWorkflowDocument CreateMultiPlatformShorts(Guid projectId)
    {
        var input = Node(VisualNodeTypes.MediaInput, "เลือกคลิป", 60, 210);
        var transcribe = Node(VisualNodeTypes.Transcribe, "ถอดเสียง", 300, 210, ("language", "auto"));
        var silence = Node(VisualNodeTypes.RemoveSilence, "ตัดเงียบสมดุล", 540, 210, ("preset", "balanced"));
        var highlights = Node(VisualNodeTypes.AnalyzeHighlights, "วิเคราะห์ Highlight", 780, 210,
            ("count", "3"), ("duration_seconds", "50"));
        var broll = Node(VisualNodeTypes.AutoBroll, "ใส่ B-roll จากคลัง", 1020, 210,
            ("source", "project_assets"), ("minimum_confidence", "0.75"));
        var approval = Node(VisualNodeTypes.VisualApproval, "ตรวจภาพประกอบ", 1260, 210);
        var tiktok = Node(VisualNodeTypes.PlatformStyle, "TikTok", 1500, 40, ("preset", "tiktok"));
        var instagram = Node(VisualNodeTypes.PlatformStyle, "Instagram Reels", 1500, 150, ("preset", "instagram_reels"));
        var facebook = Node(VisualNodeTypes.PlatformStyle, "Facebook Reels", 1500, 260, ("preset", "facebook_reels"));
        var youtube = Node(VisualNodeTypes.PlatformStyle, "YouTube Shorts", 1500, 370, ("preset", "youtube_shorts"));
        var exportTikTok = Node(VisualNodeTypes.ExportVideo, "Export TikTok", 1770, 40, ("preset", "tiktok"));
        var exportInstagram = Node(VisualNodeTypes.ExportVideo, "Export Instagram", 1770, 150, ("preset", "instagram_reels"));
        var exportFacebook = Node(VisualNodeTypes.ExportVideo, "Export Facebook", 1770, 260, ("preset", "facebook_reels"));
        var exportYoutube = Node(VisualNodeTypes.ExportVideo, "Export YouTube Shorts", 1770, 370, ("preset", "youtube_shorts"));

        return Workflow(
            projectId,
            "Shorts หลายแพลตฟอร์ม",
            "วิเคราะห์ครั้งเดียวแล้วแตกแขนงเป็น TikTok, Instagram, Facebook และ YouTube Shorts",
            [input, transcribe, silence, highlights, broll, approval, tiktok, instagram, facebook, youtube,
             exportTikTok, exportInstagram, exportFacebook, exportYoutube],
            [
                Link(input, transcribe), Link(transcribe, silence), Link(silence, highlights),
                Link(highlights, broll), Link(broll, approval),
                Link(approval, tiktok), Link(approval, instagram), Link(approval, facebook), Link(approval, youtube),
                Link(tiktok, exportTikTok), Link(instagram, exportInstagram),
                Link(facebook, exportFacebook), Link(youtube, exportYoutube)
            ]);
    }

    public VisualWorkflowDocument CreateEventRecap(Guid projectId)
    {
        var input = Node(VisualNodeTypes.FolderInput, "เลือกหลายคลิป/ทั้งโฟลเดอร์", 70, 170,
            ("sort", "captured_time"));
        var merge = Node(VisualNodeTypes.MergeClips, "เรียงและรวมคลิป", 330, 170,
            ("transition", "crossfade"), ("normalize_audio", "true"));
        var stabilize = Node(VisualNodeTypes.Stabilize, "ลดการสั่น", 590, 80, ("mode", "basic"));
        var color = Node(VisualNodeTypes.ColorCorrection, "ปรับสี", 590, 200, ("preset", "natural"));
        var audio = Node(VisualNodeTypes.EnhanceAudio, "ปรับเสียง", 590, 320,
            ("noise_reduction", "true"), ("voice_enhancement", "true"));
        var highlights = Node(VisualNodeTypes.AnalyzeHighlights, "เลือกช่วงเด่น", 850, 170,
            ("duration_seconds", "90"));
        var platform = Node(VisualNodeTypes.PlatformStyle, "YouTube 16:9", 1110, 170,
            ("preset", "youtube_landscape"));
        var export = Node(VisualNodeTypes.ExportVideo, "Export Recap", 1370, 170,
            ("preset", "youtube_landscape"));

        return Workflow(
            projectId,
            "รวมคลิปกิจกรรม",
            "นำเข้าหลายคลิป เรียงตามเวลาถ่าย ปรับภาพและเสียง เลือก Highlight แล้ว Export วิดีโอสรุป",
            [input, merge, stabilize, color, audio, highlights, platform, export],
            [
                Link(input, merge), Link(merge, stabilize), Link(merge, color), Link(merge, audio),
                Link(stabilize, highlights), Link(color, highlights), Link(audio, highlights),
                Link(highlights, platform), Link(platform, export)
            ]);
    }

    public VisualWorkflowDocument CreateTranscriptOnly(Guid projectId)
    {
        var input = Node(VisualNodeTypes.MediaInput, "เลือกคลิป", 80, 100);
        var transcribe = Node(VisualNodeTypes.Transcribe, "ถอดเสียง", 340, 100, ("language", "th"));
        var captions = Node(VisualNodeTypes.CreateCaptions, "สร้าง SRT", 600, 100, ("format", "srt"));

        return Workflow(
            projectId,
            "ถอดเสียงและสร้าง SRT",
            "ถอดเสียง Local และสร้างไฟล์ Subtitle โดยไม่ Render วิดีโอ",
            [input, transcribe, captions],
            [Link(input, transcribe), Link(transcribe, captions)]);
    }

    private static VisualWorkflowDocument Workflow(
        Guid projectId,
        string name,
        string description,
        IEnumerable<VisualWorkflowNode> nodes,
        IEnumerable<VisualWorkflowConnection> connections) => new()
    {
        ProjectId = projectId,
        Name = name,
        Description = description,
        Nodes = nodes.ToList(),
        Connections = connections.ToList()
    };

    private static VisualWorkflowNode Node(
        string type,
        string name,
        double x,
        double y,
        params (string Key, string Value)[] settings) => new()
    {
        NodeType = type,
        DisplayName = name,
        X = x,
        Y = y,
        Settings = settings.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase)
    };

    private static VisualWorkflowConnection Link(VisualWorkflowNode source, VisualWorkflowNode target) => new()
    {
        SourceNodeId = source.NodeId,
        TargetNodeId = target.NodeId
    };
}
