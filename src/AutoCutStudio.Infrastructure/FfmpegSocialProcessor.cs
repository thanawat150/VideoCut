using System.Diagnostics;
using System.Globalization;
using AutoCutStudio.Core.Interfaces;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;

namespace AutoCutStudio.Infrastructure;

public sealed class FfmpegSocialProcessor : IVideoProcessor
{
    private readonly IToolLocator _toolLocator;

    public FfmpegSocialProcessor(IToolLocator toolLocator) => _toolLocator = toolLocator;

    public async Task<ProcessingReport> ProcessAsync(
        JobDocument job,
        Func<AgentEvent, Task> emit,
        Func<JobProgress, Task> update,
        CancellationToken cancellationToken = default)
    {
        if (job.RenderRecipe is null || job.Segments.Count != 1)
        {
            throw new InvalidDataException("Social clip job requires one segment and a render recipe.");
        }

        var recipe = job.RenderRecipe;
        var input = PathSecurity.ValidateMp4Source(job.InputPath);
        var output = PathSecurity.EnsureUnderRoot(job.OutputPath, job.ProjectRoot);
        var tools = _toolLocator.Locate();
        if (!tools.IsReady || string.IsNullOrWhiteSpace(tools.FfmpegPath))
        {
            throw new InvalidOperationException(tools.Message);
        }

        var jobDirectory = PathSecurity.EnsureUnderRoot(
            Path.Combine(job.ProjectRoot, "jobs", job.JobId.ToString("N")), job.ProjectRoot);
        Directory.CreateDirectory(jobDirectory);
        if (recipe.BurnCaptions)
        {
            var expectedCaption = Path.Combine(jobDirectory, "captions.ass");
            if (recipe.CaptionAssPath is null || !File.Exists(recipe.CaptionAssPath) ||
                !string.Equals(Path.GetFullPath(recipe.CaptionAssPath), expectedCaption, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Caption file is missing or outside the job directory.");
            }
        }

        foreach (var overlay in recipe.BrollOverlays)
        {
            if (!File.Exists(overlay.AssetPath) ||
                !Path.GetFullPath(overlay.AssetPath).StartsWith(
                    Path.GetFullPath(jobDirectory) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("B-roll asset is missing or outside the job directory.");
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        if (File.Exists(output))
        {
            throw new IOException("The versioned social output already exists. Refusing to overwrite it.");
        }

        var partial = output + ".partial.mp4";
        TryDelete(partial);
        var before = new FileInfo(input);
        var arguments = BuildArguments(job, input, partial);
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

        var startedAt = DateTimeOffset.UtcNow;
        await emit(new AgentEvent
        {
            EventType = "social_render.started",
            ProjectId = job.ProjectId,
            JobId = job.JobId,
            AgentId = AgentIds.Render,
            Action = "render_social_clip",
            Status = AgentStatuses.Rendering,
            Progress = 0,
            Message = recipe.BrollOverlays.Count == 0
                ? $"เริ่มสร้างคลิป {recipe.OutputWidth}×{recipe.OutputHeight} ด้วย FFmpeg"
                : $"เริ่มสร้างคลิปพร้อม B-roll {recipe.BrollOverlays.Count} ช่วง",
            InputPath = input,
            OutputPath = output
        });

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("FFmpeg social render could not be started.");
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
                    double? eta = percentage <= 0.1
                        ? null
                        : Math.Max(0, stopwatch.Elapsed.TotalSeconds / (percentage / 100) - stopwatch.Elapsed.TotalSeconds);
                    await update(new JobProgress
                    {
                        JobId = job.JobId,
                        Status = state.Paused ? JobStatuses.Paused : JobStatuses.Exporting,
                        Progress = percentage,
                        EstimatedRemainingSeconds = eta,
                        ActiveAgentId = AgentIds.Render,
                        Message = state.Paused ? "หยุดชั่วคราว" : "กำลังสร้าง Social Clip",
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
            throw new JobCancelledException("The social clip export was cancelled by the user.");
        }
        if (process.ExitCode != 0 || !progressEnded || !File.Exists(partial) || new FileInfo(partial).Length == 0)
        {
            TryDelete(partial);
            throw new InvalidOperationException(
                $"Social FFmpeg render failed with exit code {process.ExitCode}: {LastLines(stderr, 18)}");
        }

        File.Move(partial, output, false);
        var after = new FileInfo(input);
        var sourceWasModified = before.Length != after.Length || before.LastWriteTimeUtc != after.LastWriteTimeUtc;
        await update(new JobProgress
        {
            JobId = job.JobId,
            Status = JobStatuses.Exporting,
            Progress = 100,
            EstimatedRemainingSeconds = 0,
            ActiveAgentId = AgentIds.Render,
            Message = "Social Clip สำเร็จ รอตรวจคุณภาพ",
            WorkerProcessId = Environment.ProcessId
        });
        await emit(new AgentEvent
        {
            EventType = "social_render.completed",
            ProjectId = job.ProjectId,
            JobId = job.JobId,
            AgentId = AgentIds.Render,
            Action = "render_social_clip",
            Status = AgentStatuses.Completed,
            Progress = 100,
            Message = recipe.BrollOverlays.Count > 0
                ? $"สร้าง Social Clip พร้อม B-roll {recipe.BrollOverlays.Count} ช่วงสำเร็จ"
                : recipe.BurnCaptions
                    ? "สร้าง Social Clip และ Burn Caption สำเร็จ"
                    : "สร้าง Social Clip สำเร็จ",
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
            AudioEncoder = job.ExpectedInputHasAudio ? "aac" : "none",
            SafeCommandDisplay = BuildSafeCommandDisplay(arguments),
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            SourceWasModified = sourceWasModified
        };
    }

    internal static IReadOnlyList<string> BuildArguments(JobDocument job, string input, string output)
    {
        var recipe = job.RenderRecipe!;
        var segment = job.Segments[0];
        var width = Math.Clamp(recipe.OutputWidth ?? 1080, 240, 3840);
        var height = Math.Clamp(recipe.OutputHeight ?? 1920, 240, 3840);
        var fps = Math.Clamp(recipe.OutputFrameRate ?? 30, 15, 60);
        var arguments = new List<string>
        {
            "-hide_banner", "-y", "-ss", FormatSeconds(segment.StartSeconds), "-i", input
        };

        foreach (var overlay in recipe.BrollOverlays)
        {
            var extension = Path.GetExtension(overlay.AssetPath).ToLowerInvariant();
            var duration = Math.Max(0.1, overlay.EndSeconds - overlay.StartSeconds);
            if (extension is ".jpg" or ".jpeg" or ".png" or ".webp")
            {
                arguments.AddRange([
                    "-loop", "1",
                    "-framerate", fps.ToString(CultureInfo.InvariantCulture),
                    "-t", FormatSeconds(duration),
                    "-i", overlay.AssetPath
                ]);
            }
            else
            {
                arguments.AddRange(["-stream_loop", "-1", "-i", overlay.AssetPath]);
            }
        }

        var scale = recipe.AspectStrategy == "fit_pad"
            ? $"scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:black"
            : $"scale={width}:{height}:force_original_aspect_ratio=increase,crop={width}:{height}";

        if (recipe.BrollOverlays.Count == 0)
        {
            var filters = new List<string> { scale };
            if (recipe.BurnCaptions)
            {
                filters.Add("ass=filename='captions.ass'");
            }
            arguments.AddRange(["-t", FormatSeconds(segment.DurationSeconds), "-vf", string.Join(',', filters)]);
        }
        else
        {
            var filters = new List<string> { $"[0:v:0]{scale},setpts=PTS-STARTPTS[base0]" };
            var current = "base0";
            for (var index = 0; index < recipe.BrollOverlays.Count; index++)
            {
                var overlay = recipe.BrollOverlays[index];
                var inputIndex = index + 1;
                var duration = Math.Max(0.1, overlay.EndSeconds - overlay.StartSeconds);
                var prepared = $"br{index}";
                var next = $"base{index + 1}";
                filters.Add(
                    $"[{inputIndex}:v:0]{scale},fps={fps},trim=duration={FormatSeconds(duration)},setpts=PTS-STARTPTS+{FormatSeconds(overlay.StartSeconds)}/TB[{prepared}]");
                filters.Add(
                    $"[{current}][{prepared}]overlay=0:0:eof_action=pass:enable='between(t,{FormatSeconds(overlay.StartSeconds)},{FormatSeconds(overlay.EndSeconds)})'[{next}]");
                current = next;
            }

            var videoMap = current;
            if (recipe.BurnCaptions)
            {
                filters.Add($"[{current}]ass=filename='captions.ass'[captioned]");
                videoMap = "captioned";
            }

            arguments.AddRange([
                "-t", FormatSeconds(segment.DurationSeconds),
                "-filter_complex", string.Join(';', filters),
                "-map", $"[{videoMap}]"
            ]);
            if (job.ExpectedInputHasAudio)
            {
                arguments.AddRange(["-map", "0:a:0?"]);
            }
        }

        arguments.AddRange([
            "-r", fps.ToString(CultureInfo.InvariantCulture),
            "-map_metadata", "-1",
            "-c:v", "libx264",
            "-preset", "medium",
            "-b:v", $"{Math.Clamp(recipe.VideoBitrateKbps, 800, 30000)}k",
            "-maxrate", $"{Math.Clamp(recipe.VideoBitrateKbps, 800, 30000)}k",
            "-bufsize", $"{Math.Clamp(recipe.VideoBitrateKbps * 2, 1600, 60000)}k",
            "-pix_fmt", "yuv420p"
        ]);
        if (job.ExpectedInputHasAudio)
        {
            arguments.AddRange(["-c:a", "aac", "-b:a", $"{Math.Clamp(recipe.AudioBitrateKbps, 64, 320)}k"]);
        }
        else
        {
            arguments.Add("-an");
        }
        arguments.AddRange(["-movflags", "+faststart", "-progress", "pipe:1", "-nostats", output]);
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
                                Message = "หยุด Social Render ชั่วคราว",
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

    private static string BuildSafeCommandDisplay(IEnumerable<string> arguments) =>
        "ffmpeg " + string.Join(' ', arguments.Select(argument =>
            Path.IsPathRooted(argument) ? "<media-path>" : argument));

    private static string FormatSeconds(double seconds) => seconds.ToString("0.######", CultureInfo.InvariantCulture);

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
