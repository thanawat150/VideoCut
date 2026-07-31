using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace AutoCutStudio.Rebuild;

public static class Worker
{
    public static int Doctor()
    {
        var path = Path.Combine(Path.GetTempPath(), "autocut-rebuild-doctor.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { status = "ready", ffmpeg = Tools.Find("ffmpeg"), ffprobe = Tools.Find("ffprobe"), whisper = Tools.Find("whisper-cli") }, JsonConfig.Options));
        return 0;
    }

    public static async Task<int> RunAsync(string jobPath)
    {
        JobDocument? job = null;
        try
        {
            job = await AtomicJson.ReadAsync<JobDocument>(jobPath);
            job.State = JobState.Preparing; job.StartedAt = DateTimeOffset.UtcNow; await JobStore.SaveAsync(job);
            await ExecuteAsync(job);
            job.State = JobState.Completed; job.Progress = 1; job.FinishedAt = DateTimeOffset.UtcNow; await JobStore.SaveAsync(job);
            return 0;
        }
        catch (Exception exception)
        {
            if (job is not null)
            {
                job.State = JobState.Failed; job.Error = exception.Message; job.FinishedAt = DateTimeOffset.UtcNow; await JobStore.SaveAsync(job);
                Directory.CreateDirectory(JobStore.DirectoryFor(job));
                await File.WriteAllTextAsync(Path.Combine(JobStore.DirectoryFor(job), "error.log"), exception.ToString());
            }
            return 1;
        }
    }

    private static async Task ExecuteAsync(JobDocument job)
    {
        Security.OutputUnderProject(job.ProjectRoot, job.OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(job.OutputPath)!);
        if (job.Kind == JobKind.DetectSilence) { await DetectSilence(job); return; }
        if (job.Kind == JobKind.Transcribe) { await Transcribe(job); return; }
        var partial = Path.Combine(Path.GetDirectoryName(job.OutputPath)!, Path.GetFileNameWithoutExtension(job.OutputPath) + ".partial" + Path.GetExtension(job.OutputPath));
        if (File.Exists(partial)) File.Delete(partial);
        if (job.Kind == JobKind.ExportTimeline) await ExportTimeline(job, partial); else await ProcessSingle(job, partial);
        await Progress(job, JobState.Validating, .96, "validating");
        var probe = await new FfprobeService(new SafeProcess()).ProbeAsync(partial);
        if (!probe.HasVideo) throw new InvalidDataException("Rendered output has no video stream.");
        File.Move(partial, job.OutputPath, false);
        await Progress(job, JobState.Completed, 1, "completed", job.OutputPath);
    }

    private static async Task ExportTimeline(JobDocument job, string partial)
    {
        if (job.Clips.Count == 0) throw new InvalidOperationException("Timeline is empty.");
        var runner = new SafeProcess(); var probe = new FfprobeService(runner); var segmentDirectory = Path.Combine(JobStore.DirectoryFor(job), "segments"); Directory.CreateDirectory(segmentDirectory);
        var segments = new List<string>();
        for (var index = 0; index < job.Clips.Count; index++)
        {
            var clip = job.Clips[index]; var source = Security.ExistingFile(clip.SourcePath); var meta = await probe.ProbeAsync(source); var duration = clip.SourceOutSeconds - clip.SourceInSeconds;
            var segment = Path.Combine(segmentDirectory, $"segment-{index:000}.mp4"); segments.Add(segment);
            var args = new List<string> { "-hide_banner", "-y", "-ss", F(clip.SourceInSeconds), "-t", F(duration), "-i", source };
            if (!meta.HasAudio) args.AddRange(["-f", "lavfi", "-t", F(duration), "-i", "anullsrc=channel_layout=stereo:sample_rate=48000", "-shortest"]);
            args.AddRange(["-vf", "scale=1280:720:force_original_aspect_ratio=decrease,pad=1280:720:(ow-iw)/2:(oh-ih)/2,setsar=1", "-r", "30", "-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-pix_fmt", "yuv420p", "-c:a", "aac", "-ar", "48000", "-ac", "2", segment]);
            await RunFfmpeg(job, args, index / (double)(job.Clips.Count + 1));
        }
        var concat = Path.Combine(JobStore.DirectoryFor(job), "concat.txt");
        await File.WriteAllLinesAsync(concat, segments.Select(path => $"file '{path.Replace("'", "'\\''")}'"));
        await RunFfmpeg(job, ["-hide_banner", "-y", "-f", "concat", "-safe", "0", "-i", concat, "-c", "copy", partial], .88);
    }

    private static async Task ProcessSingle(JobDocument job, string partial)
    {
        var input = Security.ExistingFile(job.InputPath ?? throw new InvalidOperationException("InputPath is required."));
        var args = new List<string> { "-hide_banner", "-y", "-i", input };
        switch (job.Kind)
        {
            case JobKind.BurnSubtitle:
                var subtitle = Security.ExistingFile(job.SubtitlePath ?? throw new InvalidOperationException("SubtitlePath is required."));
                args.AddRange(["-vf", $"subtitles={FilterPath(subtitle)}", "-c:v", "libx264", "-c:a", "copy"]); break;
            case JobKind.CreateShort: args.AddRange(["-vf", "scale=-2:1920,crop=1080:1920", "-c:v", "libx264", "-c:a", "aac"]); break;
            case JobKind.EnhanceAudio: args.AddRange(["-af", "highpass=f=80,lowpass=f=12000,afftdn,loudnorm=I=-16:TP=-1.5:LRA=11", "-c:v", "copy", "-c:a", "aac"]); break;
            case JobKind.MixVoiceover:
                var voice = Security.ExistingFile(job.VoiceoverPath ?? throw new InvalidOperationException("VoiceoverPath is required."));
                args.AddRange(["-i", voice, "-filter_complex", "[0:a]volume=0.35[bed];[bed][1:a]amix=inputs=2:duration=first:dropout_transition=2[a]", "-map", "0:v", "-map", "[a]", "-c:v", "copy", "-c:a", "aac"]); break;
            case JobKind.Stabilize: args.AddRange(["-vf", "deshake", "-c:v", "libx264", "-c:a", "copy"]); break;
            case JobKind.OverlayLogo:
                var logo = Security.ExistingFile(job.LogoPath ?? throw new InvalidOperationException("LogoPath is required."));
                args.AddRange(["-i", logo, "-filter_complex", "[1:v]scale=180:-1[logo];[0:v][logo]overlay=W-w-30:30[v]", "-map", "[v]", "-map", "0:a?", "-c:v", "libx264", "-c:a", "copy"]); break;
            case JobKind.PrivacyBlur:
                var x = job.Options.GetValueOrDefault("x", "0"); var y = job.Options.GetValueOrDefault("y", "0"); var width = job.Options.GetValueOrDefault("width", "320"); var height = job.Options.GetValueOrDefault("height", "180");
                args.AddRange(["-filter_complex", $"[0:v]crop={width}:{height}:{x}:{y},boxblur=20[blur];[0:v][blur]overlay={x}:{y}[v]", "-map", "[v]", "-map", "0:a?", "-c:v", "libx264", "-c:a", "copy"]); break;
            default: throw new NotSupportedException(job.Kind.ToString());
        }
        args.Add(partial); await RunFfmpeg(job, args, 0);
    }

    private static async Task DetectSilence(JobDocument job)
    {
        var input = Security.ExistingFile(job.InputPath ?? throw new InvalidOperationException("InputPath is required.")); var log = new List<string>();
        var exit = await new SafeProcess().RunAsync(Tools.Find("ffmpeg"), ["-hide_banner", "-i", input, "-af", "silencedetect=noise=-35dB:d=0.5", "-f", "null", "-"], null, line => log.Add(line));
        if (exit != 0) throw new InvalidOperationException("Silence detection failed.");
        await File.WriteAllLinesAsync(job.OutputPath, log.Where(x => x.Contains("silence_", StringComparison.OrdinalIgnoreCase)));
        await Progress(job, JobState.Completed, 1, "completed", job.OutputPath);
    }

    private static async Task Transcribe(JobDocument job)
    {
        var input = Security.ExistingFile(job.InputPath ?? throw new InvalidOperationException("InputPath is required.")); var model = Security.ExistingFile(job.WhisperModelPath ?? throw new InvalidOperationException("Whisper model is required."));
        var outputBase = Path.Combine(Path.GetDirectoryName(job.OutputPath)!, Path.GetFileNameWithoutExtension(job.OutputPath));
        var exit = await new SafeProcess().RunAsync(Tools.Find("whisper-cli"), ["-m", model, "-f", input, "-l", job.WhisperLanguage, "-osrt", "-otxt", "-of", outputBase], line => Progress(job, JobState.Transcribing, .5, "transcribing", line).GetAwaiter().GetResult());
        if (exit != 0) throw new InvalidOperationException("Whisper transcription failed.");
        await Progress(job, JobState.Completed, 1, "completed", outputBase + ".srt");
    }

    private static async Task RunFfmpeg(JobDocument job, IReadOnlyList<string> args, double value)
    {
        var exit = await new SafeProcess().RunAsync(Tools.Find("ffmpeg"), args, null, line => { if (line.Contains("time=", StringComparison.Ordinal)) Progress(job, JobState.Rendering, Math.Min(.94, value + .08), "rendering", line).GetAwaiter().GetResult(); });
        if (exit != 0) throw new InvalidOperationException("FFmpeg failed.");
    }

    private static Task Progress(JobDocument job, JobState state, double value, string step, string? message = null) => JobStore.SaveProgressAsync(job, new ProgressDocument { JobId = job.JobId, State = state, Progress = value, Step = step, Message = message });
    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string FilterPath(string path) => path.Replace("\\", "/").Replace(":", "\\:").Replace("'", "\\'");
}

