using System.Diagnostics;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;
using AutoCutStudio.Infrastructure;

namespace AutoCutStudio.Tests;

public sealed class SocialAutomationTests
{
    [Fact]
    public void HighlightAnalyzerReturnsRankedReviewableCandidates()
    {
        var transcript = new TranscriptDocument
        {
            ProjectId = Guid.NewGuid(),
            MediaAssetId = Guid.NewGuid(),
            SourcePath = "source.mp4",
            DetectedLanguage = "th",
            ModelName = "test",
            Segments = Enumerable.Range(0, 50).Select(index => new TranscriptSegment
            {
                Sequence = index + 1,
                StartSeconds = index * 1.2,
                EndSeconds = index * 1.2 + 1.1,
                Text = index is 8 or 9
                    ? "สิ่งสำคัญที่ต้องรู้มี 3 วิธี และนี่คือผลลัพธ์จริง"
                    : index is 30 or 31
                        ? "ทำไมปัญหานี้จึงเกิดขึ้น และวิธีแก้ที่ดีที่สุดคืออะไร"
                        : $"เนื้อหาต่อเนื่องส่วนที่ {index + 1}",
                OriginalText = "original",
                AverageProbability = 0.85
            }).ToList()
        };

        var candidates = new HighlightAnalyzer().Analyze(
            transcript,
            60,
            new HighlightAnalysisOptions
            {
                TargetDurationSeconds = 20,
                MinimumDurationSeconds = 10,
                MaximumDurationSeconds = 28,
                MaximumCandidates = 5
            });

        Assert.NotEmpty(candidates);
        Assert.True(candidates.Count <= 5);
        Assert.Equal(1, candidates[0].Rank);
        Assert.True(candidates[0].Score >= candidates[^1].Score);
        Assert.Contains(candidates, item => item.PreviewText.Contains("สำคัญ"));
        Assert.All(candidates, item => Assert.NotEmpty(item.Reasons));
    }

    [Fact]
    public void AssCaptionBuilderIncludesAnimatedCaptionHookAndCta()
    {
        var transcript = new TranscriptDocument
        {
            Segments =
            [
                new TranscriptSegment
                {
                    Sequence = 1,
                    StartSeconds = 10,
                    EndSeconds = 12,
                    Text = "ข้อความภาษาไทยสำหรับ Caption",
                    OriginalText = "ข้อความภาษาไทยสำหรับ Caption"
                }
            ]
        };
        var candidate = new HighlightCandidate { StartSeconds = 9, EndSeconds = 14 };
        var preset = new SocialExportPreset { Width = 1080, Height = 1920 };

        var ass = new AssCaptionBuilder().Build(
            transcript,
            candidate,
            preset,
            "หยุดดูตรงนี้",
            "ติดตามตอนต่อไป");

        Assert.Contains("PlayResX: 1080", ass);
        Assert.Contains("Style: Caption", ass);
        Assert.Contains("\\fad", ass);
        Assert.Contains("หยุดดูตรงนี้", ass);
        Assert.Contains("ข้อความภาษาไทยสำหรับ", ass);
        Assert.Contains("Caption", ass);
        Assert.Contains("\\N", ass);
        Assert.Contains("ติดตามตอนต่อไป", ass);
    }

    [Fact]
    public async Task RealSocialRendererCreatesVerticalCaptionedOutput()
    {
        var tools = new ToolLocator();
        var availability = tools.Locate();
        Assert.True(availability.IsReady, availability.Message);
        var root = Path.Combine(Path.GetTempPath(), "AutoCut Social ภาษาไทย " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var source = Path.Combine(root, "source clip.mp4");
            await GenerateMediaAsync(availability.FfmpegPath!, source);
            var metadata = await new FfprobeMediaProbe(tools).ProbeAsync(source);
            var media = new MediaAsset { SourcePath = source, Metadata = metadata };
            var project = new ProjectDocument
            {
                DisplayName = "Social Test",
                RootPath = root,
                SourceMedia = [media]
            };
            var candidate = new HighlightCandidate
            {
                Rank = 1,
                StartSeconds = 0.25,
                EndSeconds = 2.75,
                Score = 88,
                PreviewText = "Important result and 3 practical tips"
            };
            var transcript = new TranscriptDocument
            {
                ProjectId = project.ProjectId,
                MediaAssetId = media.Id,
                SourcePath = source,
                Segments =
                [
                    new TranscriptSegment
                    {
                        Sequence = 1,
                        StartSeconds = 0.3,
                        EndSeconds = 1.5,
                        Text = "Important result",
                        OriginalText = "Important result"
                    },
                    new TranscriptSegment
                    {
                        Sequence = 2,
                        StartSeconds = 1.5,
                        EndSeconds = 2.7,
                        Text = "Three practical tips",
                        OriginalText = "Three practical tips"
                    }
                ]
            };
            var preset = new SocialExportPreset
            {
                Id = "vertical-test",
                DisplayName = "Vertical Test",
                Width = 360,
                Height = 640,
                FrameRate = 25,
                AspectStrategy = "center_crop",
                VideoBitrateKbps = 1200,
                AudioBitrateKbps = 96
            };
            var ass = new AssCaptionBuilder().Build(transcript, candidate, preset, "HOOK", "CTA");
            var repository = new JobRepository();
            var job = await repository.CreateSocialClipJobAsync(
                project, media, candidate, preset, ass, "HOOK", "CTA", true);
            var events = new List<AgentEvent>();
            JobProgress? latestProgress = null;

            var report = await new FfmpegSocialProcessor(tools).ProcessAsync(
                job,
                item => { events.Add(item); return Task.CompletedTask; },
                item => { latestProgress = item; return Task.CompletedTask; });
            var output = await new FfprobeMediaProbe(tools).ProbeAsync(job.OutputPath);

            Assert.False(report.SourceWasModified);
            Assert.True(File.Exists(job.OutputPath));
            Assert.Equal(360, output.Width);
            Assert.Equal(640, output.Height);
            Assert.InRange(output.DurationSeconds, 2.2, 2.9);
            Assert.True(output.HasVideo);
            Assert.True(output.HasAudio);
            Assert.Equal(100, latestProgress?.Progress);
            Assert.Contains(events, item => item.EventType == "social_render.completed");
            Assert.DoesNotContain(source, report.SafeCommandDisplay, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static async Task GenerateMediaAsync(string ffmpeg, string output)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
        {
            "-hide_banner", "-y",
            "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=25:duration=3",
            "-f", "lavfi", "-i", "sine=frequency=660:sample_rate=48000:duration=3",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", output
        }) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start FFmpeg.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        _ = await stdout;
        Assert.True(process.ExitCode == 0, await stderr);
    }
}
