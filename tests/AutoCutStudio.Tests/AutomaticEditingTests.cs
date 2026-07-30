using System.Diagnostics;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;
using AutoCutStudio.Infrastructure;

namespace AutoCutStudio.Tests;

public sealed class AutomaticEditingTests
{
    [Fact]
    public void ThaiRemoveSilenceCommandIsSupported()
    {
        var parser = new AutomaticCommandParser();

        var request = parser.Parse("ช่วยตัดช่วงเงียบออกให้หน่อย");

        Assert.True(request.IsSupported);
        Assert.Equal(AutomaticEditActions.RemoveSilence, request.Action);
    }

    [Fact]
    public void UnsupportedAdvancedFeatureIsReportedHonestly()
    {
        var parser = new AutomaticCommandParser();

        var request = parser.Parse("ตรวจคำพูดผิดอัตโนมัติและหาช่วงสำคัญที่สุด");

        Assert.False(request.IsSupported);
        Assert.Contains("ยังไม่รองรับ", request.Message);
    }

    [Fact]
    public void PlannerBuildsSafeComplementAndPreservesEdgePadding()
    {
        var planner = new AutomaticEditPlanner();
        var options = new SilenceDetectionOptions
        {
            EdgePaddingSeconds = 0.1,
            MinimumOutputSegmentSeconds = 0.05
        };

        var plan = planner.BuildRemoveSilencePlan(
            "ตัดช่วงเงียบ",
            "source.mp4",
            10,
            options,
            [
                new SilenceInterval { StartSeconds = 1, EndSeconds = 2 },
                new SilenceInterval { StartSeconds = 5, EndSeconds = 6 }
            ],
            "ffmpeg <safe>");

        Assert.True(plan.IsActionable);
        Assert.Equal(3, plan.ProposedSegments.Count);
        Assert.Equal(0, plan.ProposedSegments[0].StartSeconds, 3);
        Assert.Equal(1.1, plan.ProposedSegments[0].EndSeconds, 3);
        Assert.Equal(1.9, plan.ProposedSegments[1].StartSeconds, 3);
        Assert.Equal(5.1, plan.ProposedSegments[1].EndSeconds, 3);
        Assert.Equal(5.9, plan.ProposedSegments[2].StartSeconds, 3);
        Assert.Equal(10, plan.ProposedSegments[2].EndSeconds, 3);
        Assert.Equal(1.6, plan.RemovedDurationSeconds, 3);
    }

    [Fact]
    public async Task RealFfmpegSilenceDetectionFindsTwoSilentSections()
    {
        var tools = new ToolLocator();
        var availability = tools.Locate();
        Assert.True(availability.IsReady, availability.Message);

        var directory = Path.Combine(
            Path.GetTempPath(),
            "AutoCut เงียบ ภาษาไทย " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var source = Path.Combine(directory, "เสียง และ ช่วงเงียบ.mp4");
            await GenerateSilenceTestMediaAsync(availability.FfmpegPath!, source);

            var probe = new FfprobeMediaProbe(tools);
            var metadata = await probe.ProbeAsync(source);
            Assert.True(metadata.HasAudio);
            Assert.InRange(metadata.DurationSeconds, 5.7, 6.3);

            var options = new SilenceDetectionOptions
            {
                PresetId = "balanced",
                DisplayName = "สมดุล",
                NoiseThresholdDb = -35,
                MinimumSilenceSeconds = 0.5,
                EdgePaddingSeconds = 0.12,
                MinimumOutputSegmentSeconds = 0.08
            };
            var detector = new FfmpegSilenceDetector(tools);
            var detection = await detector.DetectAsync(
                source,
                metadata.DurationSeconds,
                options);

            Assert.True(detection.Intervals.Count >= 2, detection.TechnicalLogTail);
            Assert.Contains(detection.Intervals, interval =>
                interval.StartSeconds is > 1.2 and < 1.8 &&
                interval.EndSeconds is > 2.2 and < 2.8);
            Assert.Contains(detection.Intervals, interval =>
                interval.StartSeconds is > 3.7 and < 4.3 &&
                interval.EndSeconds is > 4.7 and < 5.3);

            var planner = new AutomaticEditPlanner();
            var plan = planner.BuildRemoveSilencePlan(
                "ตัดช่วงเงียบออก",
                source,
                metadata.DurationSeconds,
                options,
                detection.Intervals,
                detection.SafeCommandDisplay);

            Assert.True(plan.IsActionable);
            Assert.True(plan.ProposedSegments.Count >= 3);
            Assert.InRange(plan.RemovedDurationSeconds, 1.2, 2.2);
            Assert.DoesNotContain(source, plan.DetectionCommand, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static async Task GenerateSilenceTestMediaAsync(string ffmpegPath, string output)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        foreach (var argument in new[]
        {
            "-hide_banner", "-y",
            "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=25:duration=6",
            "-f", "lavfi", "-i", "sine=frequency=880:sample_rate=48000:duration=1.5",
            "-f", "lavfi", "-i", "anullsrc=channel_layout=mono:sample_rate=48000:d=1",
            "-f", "lavfi", "-i", "sine=frequency=660:sample_rate=48000:duration=1.5",
            "-f", "lavfi", "-i", "anullsrc=channel_layout=mono:sample_rate=48000:d=1",
            "-f", "lavfi", "-i", "sine=frequency=550:sample_rate=48000:duration=1",
            "-filter_complex", "[1:a][2:a][3:a][4:a][5:a]concat=n=5:v=0:a=1[a]",
            "-map", "0:v:0",
            "-map", "[a]",
            "-c:v", "libx264",
            "-pix_fmt", "yuv420p",
            "-c:a", "aac",
            "-shortest",
            output
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Unable to start FFmpeg silence test generator.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var error = await errorTask;
        _ = await outputTask;

        Assert.True(process.ExitCode == 0, error);
        Assert.True(File.Exists(output));
    }
}
