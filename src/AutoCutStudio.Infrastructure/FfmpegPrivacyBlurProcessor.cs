using System.Diagnostics;
using System.Globalization;
using AutoCutStudio.Core.Interfaces;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;

namespace AutoCutStudio.Infrastructure;

public sealed class FfmpegPrivacyBlurProcessor : IVideoProcessor
{
    private readonly IToolLocator _tools;

    public FfmpegPrivacyBlurProcessor(IToolLocator tools) => _tools = tools;

    public async Task<ProcessingReport> ProcessAsync(
        JobDocument job,
        Func<AgentEvent, Task> emit,
        Func<JobProgress, Task> update,
        CancellationToken cancellationToken = default)
    {
        if (job.PrivacyBlurRecipe is null || job.PrivacyBlurRecipe.Tracks.Count == 0)
            throw new InvalidDataException("Privacy Blur Job ไม่มี Face Track ที่ผ่านการยืนยัน");
        var availability = _tools.Locate();
        if (!availability.IsReady || string.IsNullOrWhiteSpace(availability.FfmpegPath))
            throw new InvalidOperationException(availability.Message);
        var input = PathSecurity.ValidateMp4Source(job.InputPath);
        var output = PathSecurity.EnsureUnderRoot(job.OutputPath, job.ProjectRoot);
        if (File.Exists(output)) throw new IOException("Privacy output already exists; refusing to overwrite it.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var partial = output + ".partial.mp4";
        TryDelete(partial);
        var before = new FileInfo(input);
        var arguments = BuildArguments(job, input, partial);
        var startInfo = new ProcessStartInfo
        {
            FileName = availability.FfmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        await emit(new AgentEvent
        {
            EventType = "privacy_render.started",
            ProjectId = job.ProjectId,
            JobId = job.JobId,
            AgentId = AgentIds.Render,
            Action = "blur_faces",
            Status = AgentStatuses.Rendering,
            Progress = 0,
            Message = $"กำลัง Blur Face Track {job.PrivacyBlurRecipe.Tracks.Count} รายการ",
            InputPath = input,
            OutputPath = output
        });
        var startedAt = DateTimeOffset.UtcNow;
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidOperationException("ไม่สามารถเริ่ม FFmpeg Privacy Blur ได้");
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var state = new ControlState();
        var controlTask = MonitorControlAsync(process, job, state, update, linked.Token);
        var lastRounded = -1;
        var progressEnded = false;
        var stopwatch = Stopwatch.StartNew();
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
                    var percentage = Math.Clamp(seconds / Math.Max(0.001, job.ExpectedDurationSeconds) * 100, 0, 99.9);
                    state.Progress = percentage;
                    var rounded = (int)Math.Floor(percentage);
                    if (rounded <= lastRounded) continue;
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
                        Message = state.Paused ? "หยุด Privacy Blur ชั่วคราว" : "กำลัง Blur ใบหน้า",
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
            throw new JobCancelledException("Privacy Blur ถูกยกเลิก");
        }
        if (process.ExitCode != 0 || !progressEnded || !File.Exists(partial) || new FileInfo(partial).Length == 0)
        {
            TryDelete(partial);
            throw new InvalidOperationException(
                $"FFmpeg Privacy Blur ล้มเหลว (exit {process.ExitCode}): {Tail(stderr, 20)}");
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
            Message = "Privacy Blur สำเร็จ รอตรวจ QA",
            WorkerProcessId = Environment.ProcessId
        });
        await emit(new AgentEvent
        {
            EventType = "privacy_render.completed",
            ProjectId = job.ProjectId,
            JobId = job.JobId,
            AgentId = AgentIds.Render,
            Action = "blur_faces",
            Status = AgentStatuses.Completed,
            Progress = 100,
            Message = "Blur ใบหน้าตาม Face Track ที่ผู้ใช้เลือกสำเร็จ",
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
            SafeCommandDisplay = BuildSafeDisplay(arguments),
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            SourceWasModified = sourceWasModified
        };
    }

    internal static IReadOnlyList<string> BuildArguments(JobDocument job, string input, string output)
    {
        var recipe = job.PrivacyBlurRecipe!;
        var graph = BuildFilterGraph(job, out var finalLabel);
        var arguments = new List<string>
        {
            "-hide_banner", "-y", "-i", input,
            "-filter_complex", graph,
            "-map", finalLabel
        };
        if (job.ExpectedInputHasAudio)
            arguments.AddRange(["-map", "0:a?", "-c:a", "aac", "-b:a", "192k"]);
        else
            arguments.Add("-an");
        arguments.AddRange([
            "-c:v", "libx264", "-preset", "medium", "-crf", "20", "-pix_fmt", "yuv420p",
            "-movflags", "+faststart", "-progress", "pipe:1", "-nostats", output
        ]);
        return arguments;
    }

    internal static string BuildFilterGraph(JobDocument job, out string finalLabel)
    {
        var recipe = job.PrivacyBlurRecipe!;
        var intervals = new List<(FaceTrackKeyframe Keyframe, double End)>();
        foreach (var track in recipe.Tracks.Take(20))
        {
            var frames = track.Keyframes.OrderBy(item => item.TimeSeconds).Take(120).ToList();
            for (var index = 0; index < frames.Count; index++)
            {
                var end = index + 1 < frames.Count
                    ? frames[index + 1].TimeSeconds
                    : Math.Min(job.ExpectedDurationSeconds, frames[index].TimeSeconds + recipe.SampleIntervalSeconds * 1.25);
                if (end > frames[index].TimeSeconds + 0.02)
                    intervals.Add((frames[index], end));
            }
        }
        intervals = intervals.OrderBy(item => item.Keyframe.TimeSeconds).Take(160).ToList();
        if (intervals.Count == 0) throw new InvalidDataException("ไม่มี Face Keyframe ที่ใช้ Blur ได้");

        var filters = new List<string>();
        var previous = "0:v";
        for (var index = 0; index < intervals.Count; index++)
        {
            var (frame, end) = intervals[index];
            var padding = recipe.PaddingRatio;
            var x = Math.Clamp(frame.X - frame.Width * padding / 2, 0, 1);
            var y = Math.Clamp(frame.Y - frame.Height * padding / 2, 0, 1);
            var width = Math.Clamp(frame.Width * (1 + padding), 0.01, 1 - x);
            var height = Math.Clamp(frame.Height * (1 + padding), 0.01, 1 - y);
            var baseLabel = $"base{index}";
            var cropSource = $"cropSource{index}";
            var blurred = $"blur{index}";
            var outputLabel = $"v{index}";
            var xExpression = $"trunc(iw*{F(x)}/2)*2";
            var yExpression = $"trunc(ih*{F(y)}/2)*2";
            var wExpression = $"max(2,trunc(iw*{F(width)}/2)*2)";
            var hExpression = $"max(2,trunc(ih*{F(height)}/2)*2)";
            filters.Add($"[{previous}]split=2[{baseLabel}][{cropSource}]");
            filters.Add($"[{cropSource}]crop=w='{wExpression}':h='{hExpression}':x='{xExpression}':y='{yExpression}',boxblur=luma_radius={Math.Clamp(recipe.BlurStrength, 5, 40)}:luma_power=2[{blurred}]");
            filters.Add($"[{baseLabel}][{blurred}]overlay=x='{xExpression}':y='{yExpression}':enable='between(t,{F(frame.TimeSeconds)},{F(end)})'[{outputLabel}]");
            previous = outputLabel;
        }
        finalLabel = $"[{previous}]";
        return string.Join(';', filters);
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

    private static string F(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    private static string BuildSafeDisplay(IEnumerable<string> arguments) =>
        "ffmpeg " + string.Join(' ', arguments.Select(value => Path.IsPathRooted(value) ? "<local-media>" : value));
    private static string Tail(string value, int count) => string.Join(Environment.NewLine, value.Split(['\r','\n'], StringSplitOptions.RemoveEmptyEntries).TakeLast(count));
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    private sealed class ControlState { public bool Paused { get; set; } public bool Cancelled { get; set; } public double Progress { get; set; } }
}
