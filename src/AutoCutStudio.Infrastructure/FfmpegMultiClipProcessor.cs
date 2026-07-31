using System.Diagnostics;
using System.Globalization;
using AutoCutStudio.Core.Interfaces;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;

namespace AutoCutStudio.Infrastructure;

public sealed class FfmpegMultiClipProcessor : IVideoProcessor
{
    private readonly ToolLocator _toolLocator;

    public FfmpegMultiClipProcessor(ToolLocator toolLocator)
    {
        _toolLocator = toolLocator;
    }

    public async Task<ProcessingReport> ProcessAsync(
        JobDocument job,
        Func<AgentEvent, Task> emit,
        Func<JobProgress, Task> update,
        CancellationToken cancellationToken = default)
    {
        if (job.MultiClipRecipe is null || job.RenderRecipe is null || job.MultiClipRecipe.Inputs.Count < 2)
        {
            throw new InvalidDataException("Multi-clip job requires at least two inputs and a render recipe.");
        }

        var tools = _toolLocator.Locate();
        if (!tools.IsReady || string.IsNullOrWhiteSpace(tools.FfmpegPath))
        {
            throw new InvalidOperationException(tools.Message);
        }

        var orderedInputs = job.MultiClipRecipe.Inputs.OrderBy(item => item.Order).ToList();
        foreach (var input in orderedInputs)
        {
            PathSecurity.ValidateMp4Source(input.SourcePath);
        }

        var probe = new FfprobeMediaProbe(_toolLocator);
        var metadata = new List<MediaMetadata>();
        foreach (var input in orderedInputs)
        {
            metadata.Add(await probe.ProbeAsync(input.SourcePath, cancellationToken));
        }

        var output = PathSecurity.EnsureUnderRoot(job.OutputPath, job.ProjectRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        if (File.Exists(output))
        {
            throw new IOException("The versioned multi-clip output already exists. Refusing to overwrite it.");
        }

        var partial = output + ".partial.mp4";
        TryDelete(partial);
        var sourceState = orderedInputs.ToDictionary(
            input => input.SourcePath,
            input => (new FileInfo(input.SourcePath).Length, new FileInfo(input.SourcePath).LastWriteTimeUtc),
            StringComparer.OrdinalIgnoreCase);
        var arguments = BuildArguments(job, orderedInputs, metadata, partial);
        var jobDirectory = PathSecurity.EnsureUnderRoot(
            Path.Combine(job.ProjectRoot, "jobs", job.JobId.ToString("N")), job.ProjectRoot);
        var startInfo = new ProcessStartInfo
        {
            FileName = tools.FfmpegPath,
            WorkingDirectory = jobDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        await emit(new AgentEvent
        {
            EventType = "multi_clip.started",
            ProjectId = job.ProjectId,
            JobId = job.JobId,
            AgentId = AgentIds.Render,
            Action = "compose_multi_clip",
            Status = AgentStatuses.Rendering,
            Progress = 0,
            Message = $"เริ่มรวมวิดีโอ {orderedInputs.Count} คลิป ({job.MultiClipRecipe.Transition})",
            InputPath = orderedInputs[0].SourcePath,
            OutputPath = output
        });

        var startedAt = DateTimeOffset.UtcNow;
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("ไม่สามารถเริ่ม FFmpeg สำหรับรวมหลายคลิปได้");
        }

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var state = new ControlState();
        var controlTask = MonitorControlAsync(process, job, state, update, linkedCancellation.Token);
        var stopwatch = Stopwatch.StartNew();
        var lastRounded = -1;
        var progressEnded = false;

        try
        {
            while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
            {
                var separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var key = line[..separator];
                var value = line[(separator + 1)..];
                if ((key is "out_time_us" or "out_time_ms") &&
                    long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds))
                {
                    var seconds = microseconds / 1_000_000d;
                    var percentage = Math.Clamp(seconds / Math.Max(0.001, job.ExpectedDurationSeconds) * 100, 0, 99.9);
                    state.Progress = percentage;
                    var rounded = (int)Math.Floor(percentage);
                    if (rounded <= lastRounded)
                    {
                        continue;
                    }

                    lastRounded = rounded;
                    var eta = percentage <= 0.1
                        ? (double?)null
                        : Math.Max(0, stopwatch.Elapsed.TotalSeconds / (percentage / 100) - stopwatch.Elapsed.TotalSeconds);
                    await update(new JobProgress
                    {
                        JobId = job.JobId,
                        Status = state.Paused ? JobStatuses.Paused : JobStatuses.Exporting,
                        Progress = percentage,
                        EstimatedRemainingSeconds = eta,
                        ActiveAgentId = AgentIds.Render,
                        Message = state.Paused ? "หยุดชั่วคราว" : $"กำลังรวม {orderedInputs.Count} คลิป",
                        WorkerProcessId = Environment.ProcessId
                    });
                }
                else if (key == "progress" && value == "end")
                {
                    progressEnded = true;
                }
            }

            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
            throw;
        }
        finally
        {
            linkedCancellation.Cancel();
            try
            {
                await controlTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        var stderr = await stderrTask;
        if (state.Cancelled)
        {
            TryDelete(partial);
            throw new JobCancelledException("ผู้ใช้ยกเลิกงานรวมหลายคลิป");
        }

        if (process.ExitCode != 0 || !progressEnded || !File.Exists(partial) || new FileInfo(partial).Length == 0)
        {
            TryDelete(partial);
            throw new InvalidOperationException(
                $"FFmpeg รวมหลายคลิปไม่สำเร็จ (exit {process.ExitCode}): {LastLines(stderr, 24)}");
        }

        File.Move(partial, output, false);
        var sourceWasModified = sourceState.Any(pair =>
        {
            var current = new FileInfo(pair.Key);
            return current.Length != pair.Value.Length || current.LastWriteTimeUtc != pair.Value.LastWriteTimeUtc;
        });

        await update(new JobProgress
        {
            JobId = job.JobId,
            Status = JobStatuses.Exporting,
            Progress = 100,
            EstimatedRemainingSeconds = 0,
            ActiveAgentId = AgentIds.Render,
            Message = "รวมคลิปสำเร็จ รอตรวจคุณภาพ",
            WorkerProcessId = Environment.ProcessId
        });
        await emit(new AgentEvent
        {
            EventType = "multi_clip.completed",
            ProjectId = job.ProjectId,
            JobId = job.JobId,
            AgentId = AgentIds.Render,
            Action = "compose_multi_clip",
            Status = AgentStatuses.Completed,
            Progress = 100,
            Message = $"รวมวิดีโอ {orderedInputs.Count} คลิปสำเร็จ",
            InputPath = orderedInputs[0].SourcePath,
            OutputPath = output
        });

        return new ProcessingReport
        {
            JobId = job.JobId,
            InputPath = orderedInputs[0].SourcePath,
            OutputPath = output,
            Segments = [],
            Encoder = "libx264",
            AudioEncoder = "aac",
            SafeCommandDisplay = BuildSafeCommandDisplay(arguments),
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            SourceWasModified = sourceWasModified
        };
    }

    internal static IReadOnlyList<string> BuildArguments(
        JobDocument job,
        IReadOnlyList<MultiClipInput> inputs,
        IReadOnlyList<MediaMetadata> metadata,
        string output)
    {
        var render = job.RenderRecipe!;
        var recipe = job.MultiClipRecipe!;
        var width = Math.Clamp(render.OutputWidth ?? 1920, 240, 3840);
        var height = Math.Clamp(render.OutputHeight ?? 1080, 240, 3840);
        var fps = Math.Clamp(render.OutputFrameRate ?? 30, 15, 60);
        var arguments = new List<string> { "-hide_banner", "-nostdin", "-y" };
        foreach (var input in inputs)
        {
            arguments.AddRange(["-i", input.SourcePath]);
        }

        var filters = new List<string>();
        var durations = new List<double>();
        for (var index = 0; index < inputs.Count; index++)
        {
            var input = inputs[index];
            var source = metadata[index];
            var start = Math.Clamp(input.TrimStartSeconds, 0, source.DurationSeconds);
            var end = Math.Clamp(input.TrimEndSeconds ?? source.DurationSeconds, start + 0.05, source.DurationSeconds);
            var duration = Math.Max(0.05, end - start);
            durations.Add(duration);
            var videoFit = render.AspectStrategy == "fit_pad"
                ? $"scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:black"
                : $"scale={width}:{height}:force_original_aspect_ratio=increase,crop={width}:{height}";
            filters.Add(
                $"[{index}:v:0]trim=start={F(start)}:end={F(end)},setpts=PTS-STARTPTS,{videoFit},fps={fps},setsar=1,format=yuv420p[v{index}]");
            if (source.HasAudio)
            {
                var normalize = recipe.NormalizeAudio ? ",dynaudnorm=f=150:g=9" : string.Empty;
                filters.Add(
                    $"[{index}:a:0]atrim=start={F(start)}:end={F(end)},asetpts=PTS-STARTPTS,aresample=48000,aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo{normalize}[a{index}]");
            }
            else
            {
                filters.Add($"anullsrc=r=48000:cl=stereo,atrim=duration={F(duration)}[a{index}]");
            }
        }

        string videoOutput;
        string audioOutput;
        var requestedTransition = recipe.Transition.Equals("crossfade", StringComparison.OrdinalIgnoreCase)
            ? Math.Clamp(recipe.TransitionDurationSeconds, 0.1, 1.5)
            : 0;
        var transition = requestedTransition > 0
            ? Math.Min(requestedTransition, durations.Min() / 3)
            : 0;

        if (transition <= 0.01)
        {
            var concatInputs = string.Concat(Enumerable.Range(0, inputs.Count).Select(index => $"[v{index}][a{index}]"));
            filters.Add($"{concatInputs}concat=n={inputs.Count}:v=1:a=1[vout][aout]");
            videoOutput = "[vout]";
            audioOutput = "[aout]";
        }
        else
        {
            var currentVideo = "v0";
            var currentAudio = "a0";
            var cumulative = durations[0];
            for (var index = 1; index < inputs.Count; index++)
            {
                var nextVideo = $"vx{index}";
                var nextAudio = $"ax{index}";
                var offset = Math.Max(0, cumulative - transition);
                filters.Add($"[{currentVideo}][v{index}]xfade=transition=fade:duration={F(transition)}:offset={F(offset)}[{nextVideo}]");
                filters.Add($"[{currentAudio}][a{index}]acrossfade=d={F(transition)}:c1=tri:c2=tri[{nextAudio}]");
                currentVideo = nextVideo;
                currentAudio = nextAudio;
                cumulative += durations[index] - transition;
            }

            videoOutput = $"[{currentVideo}]";
            audioOutput = $"[{currentAudio}]";
        }

        arguments.AddRange([
            "-filter_complex", string.Join(';', filters),
            "-map", videoOutput,
            "-map", audioOutput,
            "-map_metadata", "-1",
            "-c:v", "libx264",
            "-preset", "medium",
            "-b:v", $"{Math.Clamp(render.VideoBitrateKbps, 800, 30000)}k",
            "-maxrate", $"{Math.Clamp(render.VideoBitrateKbps, 800, 30000)}k",
            "-bufsize", $"{Math.Clamp(render.VideoBitrateKbps * 2, 1600, 60000)}k",
            "-pix_fmt", "yuv420p",
            "-c:a", "aac",
            "-b:a", $"{Math.Clamp(render.AudioBitrateKbps, 64, 320)}k",
            "-movflags", "+faststart",
            "-progress", "pipe:1",
            "-nostats",
            output
        ]);
        return arguments;
    }

    private static async Task MonitorControlAsync(
        Process process,
        JobDocument job,
        ControlState state,
        Func<JobProgress, Task> update,
        CancellationToken cancellationToken)
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
                    var action = (await AtomicJsonFile.ReadAsync<JobControl>(path, cancellationToken))
                        .RequestedAction.Trim().ToLowerInvariant();
                    if (action != last)
                    {
                        if (action == "pause" && !state.Paused)
                        {
                            ProcessThreadController.Suspend(process);
                            state.Paused = true;
                            await update(new JobProgress
                            {
                                JobId = job.JobId,
                                Status = JobStatuses.Paused,
                                Progress = state.Progress,
                                ActiveAgentId = AgentIds.Render,
                                Message = "หยุดงานรวมคลิปชั่วคราว",
                                WorkerProcessId = Environment.ProcessId
                            });
                        }
                        else if (action == "resume" && state.Paused)
                        {
                            ProcessThreadController.Resume(process);
                            state.Paused = false;
                        }
                        else if (action == "cancel")
                        {
                            state.Cancelled = true;
                            if (state.Paused)
                            {
                                ProcessThreadController.Resume(process);
                            }
                            process.Kill(true);
                            return;
                        }
                        last = action;
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (System.Text.Json.JsonException)
            {
            }

            await Task.Delay(250, cancellationToken);
        }
    }

    private static string F(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string BuildSafeCommandDisplay(IEnumerable<string> arguments) =>
        "ffmpeg " + string.Join(' ', arguments.Select(argument =>
            Path.IsPathRooted(argument) ? "<media-path>" : argument));

    private static string LastLines(string value, int count) => string.Join(
        Environment.NewLine,
        value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).TakeLast(count));

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private sealed class ControlState
    {
        public bool Paused { get; set; }
        public bool Cancelled { get; set; }
        public double Progress { get; set; }
    }
}
