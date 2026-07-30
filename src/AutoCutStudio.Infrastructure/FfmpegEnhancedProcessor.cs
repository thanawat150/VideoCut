using System.Diagnostics;
using System.Globalization;
using AutoCutStudio.Core.Interfaces;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;

namespace AutoCutStudio.Infrastructure;

public sealed class FfmpegEnhancedProcessor : IVideoProcessor
{
    private readonly IToolLocator _tools;

    public FfmpegEnhancedProcessor(IToolLocator tools) => _tools = tools;

    public async Task<ProcessingReport> ProcessAsync(
        JobDocument job,
        Func<AgentEvent, Task> emit,
        Func<JobProgress, Task> update,
        CancellationToken cancellationToken = default)
    {
        if (job.RenderRecipe is null)
            throw new InvalidDataException("Enhancement job requires a render recipe.");
        var availability = _tools.Locate();
        if (!availability.IsReady || string.IsNullOrWhiteSpace(availability.FfmpegPath))
            throw new InvalidOperationException(availability.Message);

        var input = PathSecurity.ValidateMp4Source(job.InputPath);
        var output = PathSecurity.EnsureUnderRoot(job.OutputPath, job.ProjectRoot);
        var sourceBefore = new FileInfo(input);
        var sourceLength = sourceBefore.Length;
        var sourceModified = sourceBefore.LastWriteTimeUtc;
        var jobDirectory = PathSecurity.EnsureUnderRoot(
            Path.Combine(job.ProjectRoot, "jobs", job.JobId.ToString("N")), job.ProjectRoot);
        Directory.CreateDirectory(jobDirectory);
        var baseTimeline = Path.Combine(jobDirectory, "base-timeline.mp4");
        TryDelete(baseTimeline);

        await emit(new AgentEvent
        {
            EventType = "enhancement.started",
            ProjectId = job.ProjectId,
            JobId = job.JobId,
            AgentId = AgentIds.VideoEditor,
            Action = "prepare_enhancement",
            Status = AgentStatuses.Editing,
            Progress = 0,
            Message = "กำลังรวม Timeline ก่อนใช้ Enhancement",
            InputPath = input,
            OutputPath = output
        });

        var baseJob = job with
        {
            JobType = JobTypes.TimelineExport,
            OutputPath = baseTimeline,
            RenderRecipe = null
        };
        var timelineProcessor = new FfmpegTimelineProcessor(_tools);
        var baseReport = await timelineProcessor.ProcessAsync(
            baseJob,
            item => emit(item with
            {
                EventType = item.EventType == "render.completed" ? "enhancement.base_ready" : "enhancement." + item.EventType,
                Message = item.EventType == "render.completed" ? "รวม Timeline สำหรับ Enhancement แล้ว" : item.Message
            }),
            item => update(item with
            {
                Progress = item.Progress * 0.45,
                Message = "ขั้นที่ 1/2 — รวม Timeline"
            }),
            cancellationToken);
        if (baseReport.SourceWasModified)
            throw new InvalidDataException("Source file changed while preparing the enhancement timeline.");

        var recipe = job.RenderRecipe;
        ValidateStagedMusic(recipe.MusicPath, jobDirectory);
        var partial = output + ".partial.mp4";
        TryDelete(partial);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        if (File.Exists(output))
            throw new IOException("Enhanced output already exists; refusing to overwrite it.");

        var arguments = BuildArguments(job, baseTimeline, partial);
        var startInfo = new ProcessStartInfo
        {
            FileName = availability.FfmpegPath,
            WorkingDirectory = jobDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        var startedAt = DateTimeOffset.UtcNow;
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidOperationException("Unable to start FFmpeg enhancement process.");
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var state = new ControlState();
        var controlTask = MonitorControlAsync(process, job, state, update, linked.Token);
        var stopwatch = Stopwatch.StartNew();
        var lastRounded = -1;
        var progressEnded = false;

        try
        {
            while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
            {
                var separator = line.IndexOf('=');
                if (separator <= 0) continue;
                var key = line[..separator];
                var value = line[(separator + 1)..];
                if ((key is "out_time_us" or "out_time_ms") &&
                    long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds))
                {
                    var seconds = microseconds / 1_000_000d;
                    var local = Math.Clamp(seconds / Math.Max(0.001, job.ExpectedDurationSeconds) * 100, 0, 99.9);
                    state.Progress = 45 + local * 0.55;
                    var rounded = (int)Math.Floor(state.Progress);
                    if (rounded <= lastRounded) continue;
                    lastRounded = rounded;
                    double? eta = local <= 0.1
                        ? null
                        : Math.Max(0, stopwatch.Elapsed.TotalSeconds / (local / 100) - stopwatch.Elapsed.TotalSeconds);
                    await update(new JobProgress
                    {
                        JobId = job.JobId,
                        Status = state.Paused ? JobStatuses.Paused : JobStatuses.Exporting,
                        Progress = state.Progress,
                        EstimatedRemainingSeconds = eta,
                        ActiveAgentId = AgentIds.Render,
                        Message = state.Paused ? "หยุด Enhancement ชั่วคราว" : "ขั้นที่ 2/2 — ปรับเสียงและภาพ",
                        WorkerProcessId = Environment.ProcessId
                    });
                }
                else if (key == "progress" && value == "end") progressEnded = true;
            }
            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
            if (!process.HasExited) process.Kill(true);
            throw;
        }
        finally
        {
            linked.Cancel();
            try { await controlTask; } catch (OperationCanceledException) { }
        }

        var stderr = await stderrTask;
        if (state.Cancelled)
        {
            TryDelete(partial);
            throw new JobCancelledException("Enhancement export was cancelled.");
        }
        if (process.ExitCode != 0 || !progressEnded || !File.Exists(partial) || new FileInfo(partial).Length == 0)
        {
            TryDelete(partial);
            throw new InvalidOperationException(
                $"FFmpeg enhancement failed with exit code {process.ExitCode}: {Tail(stderr, 20)}");
        }

        File.Move(partial, output, false);
        TryDelete(baseTimeline);
        var sourceAfter = new FileInfo(input);
        var sourceWasModified = sourceAfter.Length != sourceLength || sourceAfter.LastWriteTimeUtc != sourceModified;
        await update(new JobProgress
        {
            JobId = job.JobId,
            Status = JobStatuses.Exporting,
            Progress = 100,
            EstimatedRemainingSeconds = 0,
            ActiveAgentId = AgentIds.Render,
            Message = "Enhancement สำเร็จ รอตรวจ QA",
            WorkerProcessId = Environment.ProcessId
        });
        await emit(new AgentEvent
        {
            EventType = "enhancement.completed",
            ProjectId = job.ProjectId,
            JobId = job.JobId,
            AgentId = AgentIds.Render,
            Action = "enhance_audio_visual",
            Status = AgentStatuses.Completed,
            Progress = 100,
            Message = "ปรับเสียง ภาพ และ Music Ducking สำเร็จตาม Preset ที่เลือก",
            InputPath = input,
            OutputPath = output
        });

        return new ProcessingReport
        {
            JobId = job.JobId,
            InputPath = input,
            OutputPath = output,
            Segments = job.Segments,
            Encoder = "libx264",
            AudioEncoder = "aac",
            SafeCommandDisplay = BuildSafeDisplay(arguments),
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            SourceWasModified = sourceWasModified
        };
    }

    private static IReadOnlyList<string> BuildArguments(JobDocument job, string baseTimeline, string output)
    {
        var recipe = job.RenderRecipe!;
        var videoFilters = BuildVideoFilters(recipe);
        var audioFilters = BuildAudioFilters(recipe);
        var hasMusic = !string.IsNullOrWhiteSpace(recipe.MusicPath);
        var arguments = new List<string> { "-hide_banner", "-y", "-i", baseTimeline };
        if (hasMusic) arguments.AddRange(["-stream_loop", "-1", "-i", recipe.MusicPath!]);

        if (hasMusic)
        {
            var duration = job.ExpectedDurationSeconds.ToString("0.######", CultureInfo.InvariantCulture);
            var videoChain = string.IsNullOrWhiteSpace(videoFilters) ? "null" : videoFilters;
            var voiceChain = string.IsNullOrWhiteSpace(audioFilters) ? "anull" : audioFilters;
            var volume = Math.Clamp(recipe.MusicVolume, 0, 1).ToString("0.###", CultureInfo.InvariantCulture);
            string complex;
            if (job.ExpectedInputHasAudio)
            {
                var musicChain = recipe.EnableMusicDucking
                    ? $"[1:a]volume={volume},atrim=0:{duration}[music];[music][voice]sidechaincompress=threshold=0.035:ratio=8:attack=20:release=300[ducked];[voice][ducked]amix=inputs=2:duration=first:normalize=0[aout]"
                    : $"[1:a]volume={volume},atrim=0:{duration}[music];[voice][music]amix=inputs=2:duration=first:normalize=0[aout]";
                complex = $"[0:v]{videoChain}[vout];[0:a]{voiceChain}[voice];{musicChain}";
            }
            else
            {
                complex = $"[0:v]{videoChain}[vout];[1:a]volume={volume},atrim=0:{duration}[aout]";
            }
            arguments.AddRange(["-filter_complex", complex, "-map", "[vout]", "-map", "[aout]"]);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(videoFilters)) arguments.AddRange(["-vf", videoFilters]);
            if (job.ExpectedInputHasAudio && !string.IsNullOrWhiteSpace(audioFilters)) arguments.AddRange(["-af", audioFilters]);
            if (!job.ExpectedInputHasAudio) arguments.Add("-an");
        }

        arguments.AddRange([
            "-t", job.ExpectedDurationSeconds.ToString("0.######", CultureInfo.InvariantCulture),
            "-c:v", "libx264", "-preset", "medium", "-crf", "20", "-pix_fmt", "yuv420p"
        ]);
        if (job.ExpectedInputHasAudio || hasMusic) arguments.AddRange(["-c:a", "aac", "-b:a", "192k"]);
        arguments.AddRange(["-movflags", "+faststart", "-progress", "pipe:1", "-nostats", output]);
        return arguments;
    }

    internal static string BuildVideoFilters(RenderRecipe recipe)
    {
        var filters = new List<string>();
        if (recipe.Stabilize) filters.Add("deshake=rx=16:ry=16:edge=mirror");
        filters.AddRange(recipe.ColorPreset switch
        {
            ColorPresets.Natural => ["eq=contrast=1.04:saturation=1.06:brightness=0.005"],
            ColorPresets.Vivid => ["eq=contrast=1.09:saturation=1.20:brightness=0.005"],
            ColorPresets.Warm => ["colorbalance=rs=.06:gs=.02:bs=-.05", "eq=saturation=1.08"],
            ColorPresets.Cool => ["colorbalance=rs=-.04:gs=.01:bs=.06", "eq=saturation=1.05"],
            _ => []
        });
        return string.Join(',', filters);
    }

    internal static string BuildAudioFilters(RenderRecipe recipe) => recipe.AudioEnhancementPreset switch
    {
        AudioEnhancementPresets.NoiseReduction => "afftdn=nf=-25:tn=1",
        AudioEnhancementPresets.VoiceEnhance => "highpass=f=80,lowpass=f=12000,dynaudnorm=f=150:g=15",
        AudioEnhancementPresets.NoiseAndVoice => "afftdn=nf=-25:tn=1,highpass=f=80,lowpass=f=12000,dynaudnorm=f=150:g=15",
        _ => string.Empty
    };

    private static void ValidateStagedMusic(string? path, string jobDirectory)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!File.Exists(path) || !Path.GetFullPath(path).StartsWith(Path.GetFullPath(jobDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Music must be staged inside the job directory.");
    }

    private static async Task MonitorControlAsync(
        Process process, JobDocument job, ControlState state,
        Func<JobProgress, Task> update, CancellationToken cancellationToken)
    {
        var path = Path.Combine(job.ProjectRoot, "jobs", job.JobId.ToString("N"), "control.json");
        var last = "none";
        while (!process.HasExited)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(path))
                {
                    var action = (await AtomicJsonFile.ReadAsync<JobControl>(path, cancellationToken)).RequestedAction.Trim().ToLowerInvariant();
                    if (action != last)
                    {
                        if (action == "pause" && !state.Paused) { ProcessThreadController.Suspend(process); state.Paused = true; }
                        else if (action == "resume" && state.Paused) { ProcessThreadController.Resume(process); state.Paused = false; }
                        else if (action == "cancel") { state.Cancelled = true; if (state.Paused) ProcessThreadController.Resume(process); process.Kill(true); return; }
                        last = action;
                    }
                }
            }
            catch (IOException) { }
            catch (System.Text.Json.JsonException) { }
            await Task.Delay(250, cancellationToken);
        }
    }

    private static string BuildSafeDisplay(IEnumerable<string> arguments) =>
        "ffmpeg " + string.Join(' ', arguments.Select(value => Path.IsPathRooted(value) ? "<local-media>" : value));
    private static string Tail(string value, int count) => string.Join(Environment.NewLine, value.Split(['\r','\n'], StringSplitOptions.RemoveEmptyEntries).TakeLast(count));
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    private sealed class ControlState { public bool Paused { get; set; } public bool Cancelled { get; set; } public double Progress { get; set; } }
}
