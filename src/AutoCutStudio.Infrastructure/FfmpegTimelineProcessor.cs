using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AutoCutStudio.Core.Interfaces;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;

namespace AutoCutStudio.Infrastructure;

public sealed class FfmpegTimelineProcessor : IVideoProcessor
{
    private readonly IToolLocator _toolLocator;

    public FfmpegTimelineProcessor(IToolLocator toolLocator)
    {
        _toolLocator = toolLocator;
    }

    public async Task<ProcessingReport> ProcessAsync(
        JobDocument job,
        Func<AgentEvent, Task> emitEventAsync,
        Func<JobProgress, Task> updateProgressAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(emitEventAsync);
        ArgumentNullException.ThrowIfNull(updateProgressAsync);

        var inputPath = PathSecurity.ValidateMp4Source(job.InputPath);
        var outputPath = PathSecurity.EnsureUnderRoot(job.OutputPath, job.ProjectRoot);
        PathSecurity.ValidateTimelineSegments(job.Segments, double.MaxValue);

        var tools = _toolLocator.Locate();
        if (!tools.IsReady || string.IsNullOrWhiteSpace(tools.FfmpegPath))
        {
            throw new InvalidOperationException(tools.Message);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        if (File.Exists(outputPath))
        {
            throw new IOException("The versioned output path already exists. Refusing to overwrite it.");
        }

        var partialPath = outputPath + ".partial.mp4";
        if (File.Exists(partialPath))
        {
            File.Delete(partialPath);
        }

        var sourceInfoBefore = new FileInfo(inputPath);
        var sourceLengthBefore = sourceInfoBefore.Length;
        var sourceModifiedBefore = sourceInfoBefore.LastWriteTimeUtc;

        var arguments = BuildArguments(job, inputPath, partialPath);
        var startInfo = new ProcessStartInfo
        {
            FileName = tools.FfmpegPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var safeCommandDisplay = BuildSafeCommandDisplay(tools.FfmpegPath, arguments);
        var startedAt = DateTimeOffset.UtcNow;

        await emitEventAsync(new AgentEvent
        {
            EventType = "render.started",
            ProjectId = job.ProjectId,
            JobId = job.JobId,
            AgentId = AgentIds.Render,
            Action = "export_timeline",
            Status = AgentStatuses.Rendering,
            Progress = 0,
            Message = "เริ่ม Export ด้วย FFmpeg",
            InputPath = inputPath,
            OutputPath = outputPath
        });

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new InvalidOperationException("FFmpeg could not be started.");
        }

        var runtimeState = new RuntimeControlState();
        using var monitorCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stderrTask = ReadStandardErrorAsync(process, runtimeState, cancellationToken);
        var controlTask = MonitorControlAsync(
            process,
            job,
            runtimeState,
            emitEventAsync,
            updateProgressAsync,
            monitorCancellation.Token);

        var stopwatch = Stopwatch.StartNew();
        var lastPublishedPercentage = -1;
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
                    var currentSeconds = microseconds / 1_000_000d;
                    var raw = job.ExpectedDurationSeconds <= 0
                        ? 0
                        : currentSeconds / job.ExpectedDurationSeconds;
                    var percentage = Math.Clamp(raw * 100d, 0, 99.9);
                    runtimeState.LastProgress = percentage;
                    var rounded = (int)Math.Floor(percentage);

                    if (rounded > lastPublishedPercentage)
                    {
                        lastPublishedPercentage = rounded;
                        double? eta = percentage <= 0.1
                            ? null
                            : Math.Max(0, stopwatch.Elapsed.TotalSeconds / (percentage / 100d) - stopwatch.Elapsed.TotalSeconds);

                        var progress = new JobProgress
                        {
                            JobId = job.JobId,
                            Status = runtimeState.IsPaused ? JobStatuses.Paused : JobStatuses.Exporting,
                            Progress = percentage,
                            EstimatedRemainingSeconds = eta,
                            ActiveAgentId = AgentIds.Render,
                            Message = runtimeState.IsPaused ? "หยุดชั่วคราว" : "กำลัง Export",
                            WorkerProcessId = Environment.ProcessId
                        };

                        await updateProgressAsync(progress);
                        await emitEventAsync(new AgentEvent
                        {
                            EventType = "ffmpeg.progress",
                            ProjectId = job.ProjectId,
                            JobId = job.JobId,
                            AgentId = AgentIds.Render,
                            Action = "export_timeline",
                            Status = runtimeState.IsPaused ? AgentStatuses.Paused : AgentStatuses.Rendering,
                            Progress = percentage,
                            Message = progress.Message,
                            InputPath = inputPath,
                            OutputPath = outputPath
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
            monitorCancellation.Cancel();
            try
            {
                await controlTask;
            }
            catch (OperationCanceledException)
            {
                // Expected during normal shutdown.
            }
        }

        var stderr = await stderrTask;
        if (runtimeState.CancelRequested)
        {
            TryDelete(partialPath);
            throw new JobCancelledException("The export was cancelled by the user.");
        }

        if (process.ExitCode != 0 || !progressEnded)
        {
            TryDelete(partialPath);
            var message = string.IsNullOrWhiteSpace(stderr)
                ? $"FFmpeg failed with exit code {process.ExitCode}."
                : $"FFmpeg failed with exit code {process.ExitCode}: {LastLines(stderr, 12)}";
            throw new InvalidOperationException(message);
        }

        if (!File.Exists(partialPath) || new FileInfo(partialPath).Length == 0)
        {
            TryDelete(partialPath);
            throw new InvalidDataException("FFmpeg did not create a readable output file.");
        }

        File.Move(partialPath, outputPath, overwrite: false);

        var sourceInfoAfter = new FileInfo(inputPath);
        var sourceWasModified =
            sourceInfoAfter.Length != sourceLengthBefore ||
            sourceInfoAfter.LastWriteTimeUtc != sourceModifiedBefore;

        await updateProgressAsync(new JobProgress
        {
            JobId = job.JobId,
            Status = JobStatuses.Exporting,
            Progress = 100,
            EstimatedRemainingSeconds = 0,
            ActiveAgentId = AgentIds.Render,
            Message = "FFmpeg Export สำเร็จ รอตรวจคุณภาพ",
            WorkerProcessId = Environment.ProcessId
        });

        await emitEventAsync(new AgentEvent
        {
            EventType = "ffmpeg.completed",
            ProjectId = job.ProjectId,
            JobId = job.JobId,
            AgentId = AgentIds.Render,
            Action = "export_timeline",
            Status = AgentStatuses.Completed,
            Progress = 100,
            Message = "FFmpeg สร้าง Output จริงสำเร็จ",
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
            SafeCommandDisplay = safeCommandDisplay,
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            SourceWasModified = sourceWasModified
        };
    }

    private static List<string> BuildArguments(JobDocument job, string inputPath, string outputPath)
    {
        var arguments = new List<string>
        {
            "-hide_banner",
            "-y",
            "-i", inputPath
        };

        if (job.Segments.Count == 1)
        {
            var segment = job.Segments[0];
            arguments.AddRange(
            [
                "-ss", FormatSeconds(segment.StartSeconds),
                "-t", FormatSeconds(segment.DurationSeconds)
            ]);
        }
        else
        {
            var filter = BuildConcatFilter(job.Segments, job.ExpectedInputHasAudio);
            arguments.AddRange(["-filter_complex", filter]);

            if (job.ExpectedInputHasAudio)
            {
                arguments.AddRange(["-map", "[vout]", "-map", "[aout]"]);
            }
            else
            {
                arguments.AddRange(["-map", "[vout]", "-an"]);
            }
        }

        arguments.AddRange(
        [
            "-map_metadata", "-1",
            "-c:v", "libx264",
            "-preset", "medium",
            "-crf", "20",
            "-pix_fmt", "yuv420p"
        ]);

        if (job.ExpectedInputHasAudio)
        {
            arguments.AddRange(["-c:a", "aac", "-b:a", "192k"]);
        }
        else
        {
            arguments.Add("-an");
        }

        arguments.AddRange(
        [
            "-movflags", "+faststart",
            "-progress", "pipe:1",
            "-nostats",
            outputPath
        ]);

        return arguments;
    }

    private static string BuildConcatFilter(IReadOnlyList<TimelineSegment> segments, bool hasAudio)
    {
        var builder = new StringBuilder();

        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            builder.Append(CultureInfo.InvariantCulture,
                $"[0:v:0]trim=start={segment.StartSeconds:0.######}:end={segment.EndSeconds:0.######},setpts=PTS-STARTPTS[v{index}];");

            if (hasAudio)
            {
                builder.Append(CultureInfo.InvariantCulture,
                    $"[0:a:0]atrim=start={segment.StartSeconds:0.######}:end={segment.EndSeconds:0.######},asetpts=PTS-STARTPTS[a{index}];");
            }
        }

        for (var index = 0; index < segments.Count; index++)
        {
            builder.Append($"[v{index}]");
            if (hasAudio)
            {
                builder.Append($"[a{index}]");
            }
        }

        builder.Append($"concat=n={segments.Count}:v=1:a={(hasAudio ? 1 : 0)}[vout]");
        if (hasAudio)
        {
            builder.Append("[aout]");
        }

        return builder.ToString();
    }

    private static string FormatSeconds(double seconds)
        => seconds.ToString("0.######", CultureInfo.InvariantCulture);

    private static async Task<string> ReadStandardErrorAsync(
        Process process,
        RuntimeControlState state,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        while (await process.StandardError.ReadLineAsync(cancellationToken) is { } line)
        {
            builder.AppendLine(line);
            state.LastTechnicalLine = line;
        }

        return builder.ToString();
    }

    private static async Task MonitorControlAsync(
        Process process,
        JobDocument job,
        RuntimeControlState state,
        Func<AgentEvent, Task> emitEventAsync,
        Func<JobProgress, Task> updateProgressAsync,
        CancellationToken cancellationToken)
    {
        var controlPath = Path.Combine(job.ProjectRoot, "jobs", job.JobId.ToString("N"), "control.json");
        var lastAction = "none";

        while (!process.HasExited)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (File.Exists(controlPath))
                {
                    var control = await AtomicJsonFile.ReadAsync<JobControl>(controlPath, cancellationToken);
                    var action = control.RequestedAction.Trim().ToLowerInvariant();

                    if (action != lastAction)
                    {
                        switch (action)
                        {
                            case "pause" when !state.IsPaused:
                                ProcessThreadController.Suspend(process);
                                state.IsPaused = true;
                                await updateProgressAsync(new JobProgress
                                {
                                    JobId = job.JobId,
                                    Status = JobStatuses.Paused,
                                    Progress = state.LastProgress,
                                    ActiveAgentId = AgentIds.Render,
                                    Message = "หยุด FFmpeg ชั่วคราว",
                                    WorkerProcessId = Environment.ProcessId
                                });
                                await emitEventAsync(new AgentEvent
                                {
                                    EventType = "agent.paused",
                                    ProjectId = job.ProjectId,
                                    JobId = job.JobId,
                                    AgentId = AgentIds.Render,
                                    Action = "pause_export",
                                    Status = AgentStatuses.Paused,
                                    Progress = state.LastProgress,
                                    Message = "Render Agent หยุดงานจริงชั่วคราว",
                                    InputPath = job.InputPath,
                                    OutputPath = job.OutputPath
                                });
                                break;

                            case "resume" when state.IsPaused:
                                ProcessThreadController.Resume(process);
                                state.IsPaused = false;
                                await emitEventAsync(new AgentEvent
                                {
                                    EventType = "agent.resumed",
                                    ProjectId = job.ProjectId,
                                    JobId = job.JobId,
                                    AgentId = AgentIds.Render,
                                    Action = "resume_export",
                                    Status = AgentStatuses.Rendering,
                                    Progress = state.LastProgress,
                                    Message = "Render Agent ทำงานต่อ",
                                    InputPath = job.InputPath,
                                    OutputPath = job.OutputPath
                                });
                                break;

                            case "cancel":
                                state.CancelRequested = true;
                                if (state.IsPaused)
                                {
                                    ProcessThreadController.Resume(process);
                                    state.IsPaused = false;
                                }

                                process.Kill(entireProcessTree: true);
                                return;
                        }

                        lastAction = action;
                    }
                }
            }
            catch (IOException)
            {
                // The UI may be replacing control.json atomically; retry.
            }
            catch (JsonException)
            {
                // Ignore a transient incomplete control file and retry.
            }

            await Task.Delay(250, cancellationToken);
        }
    }

    private static string BuildSafeCommandDisplay(string executable, IEnumerable<string> arguments)
    {
        static string Quote(string value)
            => value.Any(char.IsWhiteSpace) || value.Contains('"')
                ? $"\"{value.Replace("\"", "\\\"")}\""
                : value;

        return string.Join(' ', new[] { Quote(executable) }.Concat(arguments.Select(Quote)));
    }

    private static string LastLines(string value, int count)
        => string.Join(Environment.NewLine,
            value.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).TakeLast(count));

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
            // Keep the original failure as the primary error.
        }
    }

    private sealed class RuntimeControlState
    {
        public bool IsPaused { get; set; }
        public bool CancelRequested { get; set; }
        public double LastProgress { get; set; }
        public string? LastTechnicalLine { get; set; }
    }
}
