using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;
using AutoCutStudio.Infrastructure;

namespace AutoCutStudio.Tests;

public sealed class TranscriptEditingTests
{
    [Theory]
    [InlineData("ถอดเสียงภาษาไทย", AutomaticEditActions.TranscribeSpeech)]
    [InlineData("ช่วยทำซับให้หน่อย", AutomaticEditActions.CreateSubtitles)]
    [InlineData("ลบเอ่อ อ่า อืม", AutomaticEditActions.RemoveFillerWords)]
    public void AutomaticParserRoutesSpeechCommands(string command, string expectedAction)
    {
        var request = new AutomaticCommandParser().Parse(command);

        Assert.True(request.IsSupported, request.Message);
        Assert.Equal(expectedAction, request.Action);
    }

    [Fact]
    public void IsolatedFillersAreMarkedButSentenceContentIsPreserved()
    {
        var transcript = BuildTranscript(
            (0, 0.4, "เอ่อ"),
            (0.4, 1.2, "วันนี้เอ่อเราจะเริ่มงาน"),
            (1.2, 1.6, "อืม"),
            (1.6, 2.5, "เนื้อหาสำคัญ"));
        var editor = new TranscriptEditingService();

        var count = editor.MarkIsolatedFillers(transcript, excludeFromAudio: true);

        Assert.Equal(2, count);
        Assert.True(transcript.Segments[0].IsFiller);
        Assert.True(transcript.Segments[0].IsExcluded);
        Assert.False(transcript.Segments[1].IsFiller);
        Assert.False(transcript.Segments[1].IsExcluded);
        Assert.True(transcript.Segments[2].IsFiller);
        Assert.True(transcript.Segments[2].IsExcluded);
    }

    [Fact]
    public void ExcludedTranscriptRangesProduceComplementTimeline()
    {
        var transcript = BuildTranscript(
            (0, 1, "เก็บช่วงแรก"),
            (1, 2, "ตัดช่วงนี้"),
            (2, 3, "เก็บช่วงท้าย"));
        transcript.Segments[1].IsExcluded = true;
        var editor = new TranscriptEditingService();

        var timeline = editor.BuildTimelineWithoutExcluded(
            transcript,
            sourceDurationSeconds: 3,
            speechEdgePaddingSeconds: 0.05);

        Assert.Equal(2, timeline.Count);
        Assert.Equal(0, timeline[0].StartSeconds, 3);
        Assert.Equal(1.05, timeline[0].EndSeconds, 3);
        Assert.Equal(1.95, timeline[1].StartSeconds, 3);
        Assert.Equal(3, timeline[1].EndSeconds, 3);
    }

    [Fact]
    public void SrtUsesEditedTextAndSkipsExcludedSegments()
    {
        var transcript = BuildTranscript(
            (0.125, 1.5, "ข้อความแก้ไข"),
            (1.5, 2.25, "ไม่ต้องแสดง"),
            (2.25, 3.5, "บรรทัดสุดท้าย"));
        transcript.Segments[1].IsExcluded = true;
        var editor = new TranscriptEditingService();

        var srt = editor.BuildSrt(transcript);

        Assert.Contains("00:00:00,125 --> 00:00:01,500", srt);
        Assert.Contains("ข้อความแก้ไข", srt);
        Assert.DoesNotContain("ไม่ต้องแสดง", srt);
        Assert.Contains("00:00:02,250 --> 00:00:03,500", srt);
        Assert.Contains("บรรทัดสุดท้าย", srt);
    }

    [Fact]
    public async Task TranscriptRepositoryRoundTripsThaiPathsAndWritesSrt()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "AutoCut Transcript ภาษาไทย " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var transcript = BuildTranscript(
                (0, 1, "สวัสดีครับ"),
                (1, 2, "ทดสอบระบบถอดเสียง"));
            var repository = new TranscriptRepository();
            var editor = new TranscriptEditingService();

            await repository.SaveAsync(root, transcript);
            var reopened = await repository.OpenAsync(root);
            var srtPath = await repository.ExportSrtAsync(root, transcript, editor);

            Assert.NotNull(reopened);
            Assert.Equal("สวัสดีครับ", reopened.Segments[0].Text);
            Assert.True(File.Exists(srtPath));
            Assert.Contains("ทดสอบระบบถอดเสียง", await File.ReadAllTextAsync(srtPath));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static TranscriptDocument BuildTranscript(params (double Start, double End, string Text)[] values) =>
        new()
        {
            ProjectId = Guid.NewGuid(),
            MediaAssetId = Guid.NewGuid(),
            SourcePath = "source.mp4",
            DetectedLanguage = "th",
            ModelName = "test-model",
            Segments = values.Select((value, index) => new TranscriptSegment
            {
                Sequence = index + 1,
                StartSeconds = value.Start,
                EndSeconds = value.End,
                Text = value.Text,
                OriginalText = value.Text
            }).ToList()
        };
}
