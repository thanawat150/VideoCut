using System.Diagnostics;
using System.Security.Cryptography;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Infrastructure;

namespace AutoCutStudio.Tests;

public sealed class FfmpegIntegrationTests
{
    [Fact]
    public async Task RealTimelineExportPreservesSourceAndPassesFfprobeQa()
    {
        var tools = new ToolLocator();
        var availability = tools.Locate();
        Assert.True(availability.IsReady, availability.Message);

        var parent = Path.Combine(
            Path.GetTempPath(),
            "AutoCut Studio ภาษาไทย " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);

        try
        {
            var source = Path.Combine(parent, "วิดีโอ ต้นฉบับ.mp4");
            await GenerateTestMediaAsync(availability.FfmpegPath!, source);

            var sourceHashBefore = await HashAsync(source);
            var sourceWriteTimeBefore = File.GetLastWriteTimeUtc(source);

            var probe = new FfprobeMediaProbe(tools);
            var metadata = await probe.ProbeAsync(source);
            Assert.True(metadata.HasVideo);
            Assert.True(metadata.HasAudio);
            Assert.InRange(metadata.DurationSeconds, 3.8, 4.2);

            var projects = new ProjectRepository();
            var project = await projects.CreateAsync(parent, "Project ทดสอบ");
            var asset = new MediaAsset
            {
                SourcePath = source,
                Metadata = metadata
            };
            project = project with
            {
                SourceMedia = [asset],
                Timeline = new TimelineDocument
                {
                    SourceMediaId = asset.Id,
                    Segments =
                    [
                        new TimelineSegment { StartSeconds = 0.4, EndSeconds = 1.4 },
                        new TimelineSegment { StartSeconds = 2.0, EndSeconds = 3.0 }
                    ]
                }
            };
            await projects.SaveAsync(project);

            var jobs = new JobRepository();
            var job = await jobs.CreateTimelineExportJobAsync(
                project,
                asset,
                project.Timeline.Segments);

            var events = new List<AgentEvent>();
            var processor = new FfmpegTimelineProcessor(tools);
            var report = await processor.ProcessAsync(
                job,
                agentEvent =>
                {
                    events.Add(agentEvent);
                    return Task.CompletedTask;
                },
                progress => jobs.WriteProgressAsync(job, progress));

            Assert.False(report.SourceWasModified);
            Assert.True(File.Exists(job.OutputPath));
            Assert.Contains(events, item => item.EventType == "ffmpeg.progress");
            Assert.Contains(events, item => item.EventType == "ffmpeg.completed");

            var qualityControl = new OutputQualityControl(probe);
            var (qa, manifest) = await qualityControl.ValidateAsync(job);

            Assert.True(qa.Passed, string.Join("; ", qa.Errors));
            Assert.NotNull(manifest);
            Assert.True(qa.HasVideo);
            Assert.True(qa.HasAudio);
            Assert.InRange(qa.ActualDurationSeconds, 1.5, 2.5);

            var sourceHashAfter = await HashAsync(source);
            Assert.Equal(sourceHashBefore, sourceHashAfter);
            Assert.Equal(sourceWriteTimeBefore, File.GetLastWriteTimeUtc(source));

            File.WriteAllText(job.OutputPath, "occupied");
            var nextJob = await jobs.CreateTimelineExportJobAsync(
                project,
                asset,
                project.Timeline.Segments);
            Assert.NotEqual(job.OutputPath, nextJob.OutputPath);
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }

    private static async Task GenerateTestMediaAsync(string ffmpegPath, string output)
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
                     "-f", "lavfi",
                     "-i", "testsrc2=size=640x360:rate=30",
                     "-f", "lavfi",
                     "-i", "sine=frequency=1000:sample_rate=48000",
                     "-t", "4",
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
                            ?? throw new InvalidOperationException("Unable to start FFmpeg test generator.");
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var error = await errorTask;

        Assert.True(process.ExitCode == 0, error);
        Assert.True(File.Exists(output));
    }

    private static async Task<string> HashAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash);
    }
}
