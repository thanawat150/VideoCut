using System.Diagnostics;
using System.Globalization;
using System.Text;
using AutoCutStudio.Core.Interfaces;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;

namespace AutoCutStudio.Infrastructure;

public sealed class FfmpegProfessionalProcessor : IVideoProcessor
{
    private readonly IToolLocator _tools;

    public FfmpegProfessionalProcessor(IToolLocator tools) => _tools = tools;

    public async Task<ProcessingReport> ProcessAsync(
        JobDocument job,
        Func<AgentEvent, Task> emit,
        Func<JobProgress, Task> update,
        CancellationToken cancellationToken = default)
    {
        var availability = _tools.Locate();
        if (!availability.IsReady || string.IsNullOrWhiteSpace(availability.FfmpegPath))
            throw new InvalidOperationException(availability.Message);

        var sources = GetSourcePaths(job).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (sources.Count == 0) throw new InvalidDataException("Professional job ไม่มี Source Media");
        foreach (var source in sources) PathSecurity.ValidateMp4Source(source);
        var snapshots = sources.ToDictionary(
            path => path,
            path => (new FileInfo(path).Length, new FileInfo(path).LastWriteTimeUtc),
            StringComparer.OrdinalIgnoreCase);

        var output = PathSecurity.EnsureUnderRoot(job.OutputPath, job.ProjectRoot);
        if (File.Exists(output)) throw new IOException("Professional output already exists; refusing to overwrite it.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var partial = output + ".partial.mp4";
        TryDelete(partial);
        var arguments = BuildArguments(job, partial);
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
            EventType = "professional_render.started",
            ProjectId = job.ProjectId,
            JobId = job.JobId,
            AgentId = AgentIds.Render,
            Action = job.JobType,
            Status = AgentStatuses.Rendering,
            Progress = 0,
            Message = $"เริ่ม Render {job.JobType}",
            InputPath = job.InputPath,
            OutputPath = output
        });

        var startedAt = DateTimeOffset.UtcNow;
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidOperationException("ไม่สามารถเริ่ม FFmpeg Professional Render ได้");
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var controlCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var state = new ControlState();
        var controlTask = MonitorControlAsync(process, job, state, update, controlCancellation.Token);
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
                        Message = state.Paused ? "หยุดงานชั่วคราว" : $"กำลัง Render {job.JobType}",
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
            controlCancellation.Cancel();
            try { await controlTask; } catch (OperationCanceledException) { }
        }

        var stderr = await stderrTask;
        if (state.Cancelled)
        {
            TryDelete(partial);
            throw new JobCancelledException("Professional Render ถูกยกเลิก");
        }
        if (process.ExitCode != 0 || !progressEnded || !File.Exists(partial) || new FileInfo(partial).Length == 0)
        {
            TryDelete(partial);
            throw new InvalidOperationException($"FFmpeg Professional Render ล้มเหลว (exit {process.ExitCode}): {Tail(stderr, 24)}");
        }
        File.Move(partial, output, false);

        var sourceWasModified = sources.Any(path =>
        {
            var current = new FileInfo(path);
            var before = snapshots[path];
            return current.Length != before.Length || current.LastWriteTimeUtc != before.LastWriteTimeUtc;
        });
        await update(new JobProgress
        {
            JobId = job.JobId,
            Status = JobStatuses.Exporting,
            Progress = 100,
            EstimatedRemainingSeconds = 0,
            ActiveAgentId = AgentIds.Render,
            Message = "Professional Render สำเร็จ รอตรวจ QA",
            WorkerProcessId = Environment.ProcessId
        });
        await emit(new AgentEvent
        {
            EventType = "professional_render.completed",
            ProjectId = job.ProjectId,
            JobId = job.JobId,
            AgentId = AgentIds.Render,
            Action = job.JobType,
            Status = AgentStatuses.Completed,
            Progress = 100,
            Message = $"Render {job.JobType} สำเร็จ",
            InputPath = job.InputPath,
            OutputPath = output
        });
        return new ProcessingReport
        {
            JobId = job.JobId,
            InputPath = job.InputPath,
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

    internal static IReadOnlyList<string> BuildArguments(JobDocument job, string output)
    {
        return job.JobType switch
        {
            ProfessionalJobTypes.MulticamExport => BuildMulticamArguments(job, output),
            ProfessionalJobTypes.KeyframeExport => BuildKeyframeArguments(job, output),
            ProfessionalJobTypes.NestedSequenceExport => BuildNestedArguments(job, output),
            _ => throw new InvalidDataException($"Unsupported professional job type: {job.JobType}")
        };
    }

    private static IReadOnlyList<string> BuildMulticamArguments(JobDocument job, string output)
    {
        var recipe = job.MulticamRecipe ?? throw new InvalidDataException("Multicam Recipe missing");
        var sources = recipe.Angles.Select(item => item.SourcePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var inputIndex = sources.Select((path, index) => new { path, index })
            .ToDictionary(item => item.path, item => item.index, StringComparer.OrdinalIgnoreCase);
        var arguments = BaseInputs(sources);
        var clips = recipe.Switches.OrderBy(item => item.Sequence).Select(item =>
        {
            var angle = recipe.Angles.Single(value => value.AngleId == item.AngleId);
            return new RenderClip(
                inputIndex[angle.SourcePath],
                Math.Max(0, item.SourceStartSeconds + angle.OffsetSeconds),
                Math.Max(0, item.SourceEndSeconds + angle.OffsetSeconds),
                angle.HasAudio);
        }).ToList();
        AppendConcat(arguments, clips, recipe.OutputWidth, recipe.OutputHeight, recipe.FrameRate, job.ExpectedInputHasAudio, output);
        return arguments;
    }

    private static IReadOnlyList<string> BuildNestedArguments(JobDocument job, string output)
    {
        var recipe = job.NestedSequenceRecipe ?? throw new InvalidDataException("Nested Sequence Recipe missing");
        var sources = recipe.Clips.Select(item => item.SourcePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var inputIndex = sources.Select((path, index) => new { path, index })
            .ToDictionary(item => item.path, item => item.index, StringComparer.OrdinalIgnoreCase);
        var arguments = BaseInputs(sources);
        var clips = recipe.Clips.OrderBy(item => item.Sequence)
            .Select(item => new RenderClip(inputIndex[item.SourcePath], item.SourceStartSeconds, item.SourceEndSeconds, item.HasAudio))
            .ToList();
        AppendConcat(arguments, clips, recipe.OutputWidth, recipe.OutputHeight, recipe.FrameRate, job.ExpectedInputHasAudio, output);
        return arguments;
    }

    private static IReadOnlyList<string> BuildKeyframeArguments(JobDocument job, string output)
    {
        var recipe = job.KeyframeRecipe ?? throw new InvalidDataException("Keyframe Recipe missing");
        var frames = recipe.Keyframes.OrderBy(item => item.TimeSeconds).ToList();
        if (frames.Count < 2) throw new InvalidDataException("Keyframe Recipe requires at least two keyframes");
        var arguments = BaseInputs([job.InputPath]);
        var graph = new StringBuilder();
        var intervalCount = frames.Count - 1;
        for (var index = 0; index < intervalCount; index++)
        {
            var left = frames[index];
            var right = frames[index + 1];
            var duration = right.TimeSeconds - left.TimeSeconds;
            var progress = $"min(1,max(0,t/{F(duration)}))";
            var zoom = $"({F(left.Zoom)}+({F(right.Zoom - left.Zoom)})*{progress})";
            var focusX = $"({F(left.FocusX)}+({F(right.FocusX - left.FocusX)})*{progress})";
            var focusY = $"({F(left.FocusY)}+({F(right.FocusY - left.FocusY)})*{progress})";
            graph.Append($"[0:v:0]trim=start={F(left.TimeSeconds)}:end={F(right.TimeSeconds)},setpts=PTS-STARTPTS,");
            graph.Append($"crop=w='max(2,trunc(iw/{zoom}/2)*2)':h='max(2,trunc(ih/{zoom}/2)*2)':");
            graph.Append($"x='max(0,min(iw-out_w,(iw-out_w)*{focusX}))':y='max(0,min(ih-out_h,(ih-out_h)*{focusY}))':eval=frame,");
            graph.Append($"scale={ClampWidth(recipe.OutputWidth)}:{ClampHeight(recipe.OutputHeight)}:flags=lanczos,fps={ClampFps(recipe.FrameRate)},format=yuv420p[v{index}];");
            if (job.ExpectedInputHasAudio)
            {
                var gain = $"({F(left.AudioGain)}+({F(right.AudioGain - left.AudioGain)})*{progress})";
                graph.Append($"[0:a:0]atrim=start={F(left.TimeSeconds)}:end={F(right.TimeSeconds)},asetpts=PTS-STARTPTS,volume='{gain}':eval=frame[a{index}];");
            }
        }
        for (var index = 0; index < intervalCount; index++)
        {
            graph.Append($"[v{index}]");
            if (job.ExpectedInputHasAudio) graph.Append($"[a{index}]");
        }
        graph.Append($"concat=n={intervalCount}:v=1:a={(job.ExpectedInputHasAudio ? 1 : 0)}[vout]");
        if (job.ExpectedInputHasAudio) graph.Append("[aout]");
        arguments.AddRange(["-filter_complex", graph.ToString(), "-map", "[vout]"]);
        if (job.ExpectedInputHasAudio) arguments.AddRange(["-map", "[aout]"]);
        else arguments.Add("-an");
        AppendOutput(arguments, job.ExpectedInputHasAudio, output);
        return arguments;
    }

    private static List<string> BaseInputs(IReadOnlyList<string> sources)
    {
        var arguments = new List<string> { "-hide_banner", "-y" };
        foreach (var source in sources) arguments.AddRange(["-i", source]);
        return arguments;
    }

    private static void AppendConcat(
        List<string> arguments,
        IReadOnlyList<RenderClip> clips,
        int width,
        int height,
        int frameRate,
        bool includeAudio,
        string output)
    {
        var graph = new StringBuilder();
        for (var index = 0; index < clips.Count; index++)
        {
            var clip = clips[index];
            graph.Append($"[{clip.InputIndex}:v:0]trim=start={F(clip.Start)}:end={F(clip.End)},setpts=PTS-STARTPTS,");
            graph.Append($"scale={ClampWidth(width)}:{ClampHeight(height)}:force_original_aspect_ratio=decrease,");
            graph.Append($"pad={ClampWidth(width)}:{ClampHeight(height)}:(ow-iw)/2:(oh-ih)/2,fps={ClampFps(frameRate)},format=yuv420p[v{index}];");
            if (includeAudio)
                graph.Append($"[{clip.InputIndex}:a:0]atrim=start={F(clip.Start)}:end={F(clip.End)},asetpts=PTS-STARTPTS[a{index}];");
        }
        for (var index = 0; index < clips.Count; index++)
        {
            graph.Append($"[v{index}]");
            if (includeAudio) graph.Append($"[a{index}]");
        }
        graph.Append($"concat=n={clips.Count}:v=1:a={(includeAudio ? 1 : 0)}[vout]");
        if (includeAudio) graph.Append("[aout]");
        arguments.AddRange(["-filter_complex", graph.ToString(), "-map", "[vout]"]);
        if (includeAudio) arguments.AddRange(["-map", "[aout]"]);
        else arguments.Add("-an");
        AppendOutput(arguments, includeAudio, output);
    }

    private static void AppendOutput(List<string> arguments, bool includeAudio, string output)
    {
        arguments.AddRange(["-c:v", "libx264", "-preset", "medium", "-crf", "20", "-pix_fmt", "yuv420p"]);
        if (includeAudio) arguments.AddRange(["-c:a", "aac", "-b:a", "192k"]);
        arguments.AddRange(["-movflags", "+faststart", "-progress", "pipe:1", "-nostats", output]);
    }

    private static IReadOnlyList<string> GetSourcePaths(JobDocument job) => job.JobType switch
    {
        ProfessionalJobTypes.MulticamExport => job.MulticamRecipe?.Angles.Select(item => item.SourcePath).ToList() ?? [],
        ProfessionalJobTypes.KeyframeExport => [job.InputPath],
        ProfessionalJobTypes.NestedSequenceExport => job.NestedSequenceRecipe?.Clips.Select(item => item.SourcePath).ToList() ?? [],
        _ => []
    };

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
                        if (action == "pause" && !state.Paused)
                        {
                            ProcessThreadController.Suspend(process);
                            state.Paused = true;
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

    private static int ClampWidth(int value) => Math.Clamp(value, 320, 3840) / 2 * 2;
    private static int ClampHeight(int value) => Math.Clamp(value, 240, 3840) / 2 * 2;
    private static int ClampFps(int value) => Math.Clamp(value, 15, 60);
    private static string F(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    private static string BuildSafeDisplay(IEnumerable<string> arguments) =>
        "ffmpeg " + string.Join(' ', arguments.Select(value => Path.IsPathRooted(value) ? "<local-media>" : value));
    private static string Tail(string value, int count) =>
        string.Join(Environment.NewLine, value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).TakeLast(count));
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    private sealed record RenderClip(int InputIndex, double Start, double End, bool HasAudio);
    private sealed class ControlState { public bool Paused { get; set; } public bool Cancelled { get; set; } public double Progress { get; set; } }
}
