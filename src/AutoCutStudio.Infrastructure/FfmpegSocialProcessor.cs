using System.Diagnostics;
using System.Globalization;
using System.Text;
using AutoCutStudio.Core.Interfaces;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;

namespace AutoCutStudio.Infrastructure;

public sealed class FfmpegSocialProcessor : IVideoProcessor
{
    private readonly IToolLocator _toolLocator;

    public FfmpegSocialProcessor(IToolLocator toolLocator)
    {
        _toolLocator = toolLocator;
    }

    public async Task<ProcessingReport> ProcessAsync(
        JobDocument job,
        Func<AgentEvent, Task> emitEventAsync,
        Func<JobProgress, Task> updateProgressAsync,
        CancellationToken cancellationToken = default)
    {
        if (job.RenderRecipe is null || job.Segments.Count != 1)
        {
            throw new InvalidDataException("Social clip job requires one segment and a render recipe.");
        }

        var recipe = job.RenderRecipe;
        var segment = job.Segments[0];
        var inputPath = PathSecurity.ValidateMp4Source(job.InputPath);
        var outputPath = PathSecurity.EnsureUnderRoot(job.OutputPath, job.ProjectRoot);
        var tools = _toolLocator.Locate();
        if (!tools.IsReady || string.IsNullOrWhiteSpace(tools.FfmpegPath))
        {
            throw new InvalidOperationException(tools.Message);
        }

        var jobDirectory = Path.Combine(job.ProjectRoot, "jobs", job.JobId.ToString("N"));
        Directory.CreateDirectory(jobDirectory);
        if (recipe.BurnCaptions)
        {
            var captionPath = recipe.CaptionAssPath ?? string.Empty;
            if (!File.Exists(captionPath) ||
                !string.Equals(Path.GetFullPath(captionPath), Path.Combine(jobDirectory, "captions.ass"), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Caption file is missing or outside the job directory.");
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        if (File.Exists(outputPath))
        {
            throw new IOException("The versioned social output already exists. Refusing to overwrite it.");
        }

        var partialPath = outputPath + ".partial.mp4";
        TryDelete(partialPath);
        var sourceBefore = new FileInfo(inputPath);
        var sourceLength = sourceBefore.Length;
        var sourceModified = sourceBefore.LastWriteTimeUtc;
        var arguments = BuildArguments(job, inputPath, partialPath);
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

        var safeCommand = BuildSafeCommandDisplay(arguments);
        var startedAt = DateTimeOffset.UtcNow;
        await emitEventAsync(new AgentEvent
        {
            EventType = "social_render.started",
            ProjectId = job.ProjectId,
            JobId = job.JobId,
            AgentId = AgentIds.Render,
            Action = "render_social_clip",
            Status = AgentStatuses.Rendering,
            Progress = 0,
            Message = $"เริ่มสร้างคลิป {recipe.OutputWidth}×{recipe.OutputHeight} ด้วย FFmpeg",
            InputPath = inputPath,
            OutputPath = outputPath
        });

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("FFmpeg social render could not be started.");
        }

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var controlCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var controlState = new ControlState();
        var controlTask = MonitorControlAsync(
            process,
            job,
            controlState,
            emitEventAsync,
            updateProgressAsync,
            controlCancellation.Token);
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
                    controlState.Progress = percentage;
                    var rounded = (int)Math.Floor(percentage);
                    if (rounded > lastRounded)
                    {
                        lastRounded = rounded;
                        var eta = percentage <= 0.1
                            ? null
                            : Math.Max(0, stopwatch.Elapsed.TotalSeconds / (percentage / 100) - stopwatch.Elapsed.TotalSeconds);
                        await updateProgressAsync(new JobProgress
                        {
                            JobId = job.JobId,
                            Status = controlState.Paused ? JobStatuses.Paused : JobStatuses.Exporting,
                            Progress = percentage,
                            EstimatedRemainingSeconds = eta,
                            ActiveAgentId = AgentIds.Render,
                            Message = controlState.Paused ? "หยุดชั่วคราว" : "กำลังสร้าง Social Clip",
                            WorkerProcessId = Environment.ProcessId
                        });
                    }
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
                process.Kill(entireProcessTree: true);
            }
            throw;
        }
        finally
        {
            controlCancellation.Cancel();
            try { await controlTask; } catch (OperationCanceledException) { }
        }

        var stderr = await stderrTask;
        if (controlState.Cancelled)
        {
            TryDelete(partialPath);
            throw new JobCancelledException("The social clip export was cancelled by the user.");
        }

        if (process.ExitCode != 0 || !progressEnded || !File.Exists(partialPath) || new FileInfo(partialPath).Length == 0)
        {
            TryDelete(partialPath);
            throw new InvalidOperationException(
                $"Social FFmpeg render failed with exit code {process.ExitCode}: {LastLines(stderr, 18)}");
        }

        File.Move(partialPath, outputPath, false);
        var sourceAfter = new FileInfo(inputPath);
        var sourceWasModified = sourceAfter.Length != sourceLength || sourceAfter.LastWriteTimeUtc != sourceModified;
        await updateProgressAsync(new JobProgress
        {
            JobId = job.JobId,
            Status = JobStatuses.Exporting,
            Progress = 100,
            EstimatedRemainingSeconds = 0,
            ActiveAgentId = AgentIds.Render,
            Message = "Social Clip สำเร็จ รอตรวจคุณภาพ",
            WorkerProcessId = Environment.ProcessId
        });
        await emitEventAsync(new AgentEvent
        {
            EventType = "social_render.completed",
            ProjectId = job.ProjectId,
            JobId = job.JobId,
            AgentId = AgentIds.Render,
            Action = "render_social_clip",
            Status = AgentStatuses.Completed,
            Progress = 100,
            Message = "สร้าง Social Clip และ Burn Caption สำเร็จ",
            InputPath = inputPath,
            OutputPath = outputPath
        });

        return new ProcessingReport
        {
            JobId = job.JobId,
            InputPath = inputPath,
            OutputPath = outputPath,
            Segments = job.Segments,
            Encoder = "libx264",
            AudioEncoder = job.ExpectedInputHasAudio ? "aac" : "none",
            SafeCommandDisplay = safeCommand,
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            SourceWasModified = sourceWasModified
        };
    }

    private static IReadOnlyList<string> BuildArguments(JobDocument job, string inputPath, string outputPath)
    {
        var recipe = job.RenderRecipe!;
        var segment = job.Segments[0];
        var width = Math.Clamp(recipe.OutputWidth ?? 1080, 240, 3840);
        var height = Math.Clamp(recipe.OutputHeight ?? 1920, 240, 3840);
        var fps = Math.Clamp(recipe.OutputFrameRate ?? 30, 15, 60);
        var filters = new List<string>();
        filters.Add(recipe.AspectStrategy == "fit_pad"
            ? $"scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:black"
            : $"scale={width}:{height}:force_original_aspect_ratio=increase,crop={width}:{height}");
        if (recipe.BurnCaptions)
        {
            filters.Add("ass=filename='captions.ass'");
        }

        return new List<string>
        {
            "-hide_banner", "-y",
            "-ss", FormatSeconds(segment.StartSeconds),
            "-i", inputPath,
            "-t", FormatSeconds(segment.DurationSeconds),
            "-vf", string.Join(',', filters),
            "-r", fps.ToString(CultureInfo.InvariantCulture),
            "-map_metadata", "-1",
            "-c:v", "libx264",
            "-preset", "medium",
            "-b:v", $"{Math.Clamp(recipe.VideoBitrateKbps, 800, 30000)}k",
            "-maxrate", $"{Math.Clamp(recipe.VideoBitrateKbps, 800, 30000)}k",
            "-bufsize", $"{Math.Clamp(recipe.VideoBitrateKbps * 2, 1600, 60000)}k",
            "-pix_fmt", "yuv420p",
            job.ExpectedInputHasAudio ? "-c:a" : "-an",
            job.ExpectedInputHasAudio ? "aac" : string.Empty,
            job.ExpectedInputHasAudio ? "-b:a" : string.Empty,
            job.ExpectedInputHasAudio ? $"{Math.Clamp(recipe.AudioBitrateKbps, 64, 320)}k" : string.Empty,
            "-movflags", "+faststart",
            "-progress", "pipe:1",
            "-nostats",
            outputPath
        }.Where(value => !string.IsNullOrEmpty(value)).ToList();
    }

    private static async Task MonitorControlAsync(
        Process process,
        JobDocument job,
        ControlState state,
        Func<AgentEvent, Task> emit,
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
                    var control = await AtomicJsonFile.ReadAsync<JobControl>(path, cancellationToken);
                    var action = control.RequestedAction.Trim().ToLowerInvariant();
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
                            if (state.Paused) ProcessThreadController.Resume(process);
                            process.Kill(true);
                            return;
                        }
                        last = action;
                    }
                }
            }
            catch (IOException) { }
            catch (System.Text.Json.JsonException) { }
            await Task.Delay(250, cancellationToken);
        }
    }

    private static string BuildSafeCommandDisplay(IEnumerable<string> arguments) =>
        "ffmpeg " + string.Join(' ', arguments.Select(argument =>
            argument == "captions.ass" || !Path.IsPathRooted(argument)
                ? argument
                : argument.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ? "<media-path>" : argument));

    private static string FormatSeconds(double seconds) => seconds.ToString("0.######", CultureInfo.InvariantCulture);
    private static string LastLines(string value, int count) => string.Join(Environment.NewLine, value.Split(['\r','\n'], StringSplitOptions.RemoveEmptyEntries).TakeLast(count));
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }

    private sealed class ControlState
    {
        public bool Paused { get; set; }
        public bool Cancelled { get; set; }
        public double Progress { get; set; }
    }
}
