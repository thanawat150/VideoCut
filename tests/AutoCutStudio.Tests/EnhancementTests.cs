using System.Diagnostics;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Infrastructure;

namespace AutoCutStudio.Tests;

public sealed class EnhancementTests
{
    [Fact]
    public async Task RealBeatDetectorFindsRegularPulseTempo()
    {
        var tools = new ToolLocator();
        var availability = tools.Locate();
        Assert.True(availability.IsReady, availability.Message);
        var root = Path.Combine(Path.GetTempPath(), "AutoCut Beat ภาษาไทย " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var audio = Path.Combine(root, "click track.wav");
            await GenerateClickTrackAsync(availability.FfmpegPath!, audio);

            var result = await new FfmpegBeatDetector(tools).AnalyzeAsync(audio, 6);

            Assert.True(result.Beats.Count >= 7, $"Detected only {result.Beats.Count} beats.");
            Assert.InRange(result.EstimatedBpm, 105, 135);
            Assert.All(result.Beats, beat => Assert.True(beat.TimeSeconds >= 0));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RealEnhancementProcessorCreatesDuckedMusicOutputAndPreservesSource()
    {
        var tools = new ToolLocator();
        var availability = tools.Locate();
        Assert.True(availability.IsReady, availability.Message);
        var root = Path.Combine(Path.GetTempPath(), "AutoCut Enhance ภาษาไทย " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var source = Path.Combine(root, "source noisy voice.mp4");
            var music = Path.Combine(root, "background music.wav");
            await GenerateSourceAsync(availability.FfmpegPath!, source);
            await GenerateMusicAsync(availability.FfmpegPath!, music);
            var beforeHash = await ComputeSha256Async(source);
            var metadata = await new FfprobeMediaProbe(tools).ProbeAsync(source);
            var media = new MediaAsset { SourcePath = source, Metadata = metadata };
            var project = new ProjectDocument
            {
                DisplayName = "Enhancement Test",
                RootPath = root,
                SourceMedia = [media]
            };
            var segments = new List<TimelineSegment>
            {
                new() { StartSeconds = 0.2, EndSeconds = 3.6 }
            };
            var plan = new EnhancementPlan
            {
                AudioPreset = AudioEnhancementPresets.NoiseAndVoice,
                ColorPreset = ColorPresets.Natural,
                Stabilize = true,
                MusicPath = music,
                EnableMusicDucking = true,
                MusicVolume = 0.2
            };
            var repository = new JobRepository();
            var job = await new EnhancementJobFactory(repository).CreateAsync(
                project, media, segments, plan);
            var events = new List<AgentEvent>();
            JobProgress? latest = null;

            var report = await new FfmpegEnhancedProcessor(tools).ProcessAsync(
                job,
                item => { events.Add(item); return Task.CompletedTask; },
                item => { latest = item; return Task.CompletedTask; });
            var output = await new FfprobeMediaProbe(tools).ProbeAsync(job.OutputPath);
            var afterHash = await ComputeSha256Async(source);

            Assert.False(report.SourceWasModified);
            Assert.Equal(beforeHash, afterHash);
            Assert.True(File.Exists(job.OutputPath));
            Assert.True(output.HasVideo);
            Assert.True(output.HasAudio);
            Assert.Equal(metadata.Width, output.Width);
            Assert.Equal(metadata.Height, output.Height);
            Assert.InRange(output.DurationSeconds, 3.1, 3.8);
            Assert.Equal(100, latest?.Progress);
            Assert.Contains(events, item => item.EventType == "enhancement.completed");
            Assert.DoesNotContain(source, report.SafeCommandDisplay, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(music, report.SafeCommandDisplay, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static async Task GenerateClickTrackAsync(string ffmpeg, string output)
    {
        var expression = "aevalsrc=if(lt(mod(t\\,0.5)\\,0.045)\\,0.95*sin(2*PI*1000*t)\\,0):s=8000:d=6";
        await RunFfmpegAsync(ffmpeg,
            "-hide_banner", "-y",
            "-f", "lavfi", "-i", expression,
            "-c:a", "pcm_s16le", output);
    }

    private static async Task GenerateSourceAsync(string ffmpeg, string output)
    {
        await RunFfmpegAsync(ffmpeg,
            "-hide_banner", "-y",
            "-f", "lavfi", "-i", "testsrc2=size=480x270:rate=25:duration=4",
            "-f", "lavfi", "-i", "sine=frequency=730:sample_rate=48000:duration=4",
            "-f", "lavfi", "-i", "anoisesrc=color=white:amplitude=0.025:sample_rate=48000:duration=4",
            "-filter_complex", "[1:a][2:a]amix=inputs=2:duration=first[a]",
            "-map", "0:v:0", "-map", "[a]",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", output);
    }

    private static async Task GenerateMusicAsync(string ffmpeg, string output)
    {
        await RunFfmpegAsync(ffmpeg,
            "-hide_banner", "-y",
            "-f", "lavfi", "-i", "sine=frequency=220:sample_rate=48000:duration=5",
            "-af", "volume=0.35", "-c:a", "pcm_s16le", output);
    }

    private static async Task RunFfmpegAsync(string ffmpeg, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start FFmpeg.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        _ = await stdout;
        Assert.True(process.ExitCode == 0, await stderr);
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash);
    }
}
