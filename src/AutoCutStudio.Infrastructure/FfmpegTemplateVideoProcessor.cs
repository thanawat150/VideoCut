using System.Diagnostics;
using System.Globalization;
using AutoCutStudio.Core.Interfaces;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;

namespace AutoCutStudio.Infrastructure;

public sealed class FfmpegTemplateVideoProcessor : IVideoProcessor
{
    private readonly IToolLocator _tools;

    public FfmpegTemplateVideoProcessor(IToolLocator tools) => _tools = tools;

    public async Task<ProcessingReport> ProcessAsync(
        JobDocument job,
        Func<AgentEvent, Task> emit,
        Func<JobProgress, Task> update,
        CancellationToken cancellationToken = default)
    {
        if (job.TemplateVideoRecipe is null)
            throw new InvalidDataException("Template Video Job ไม่มี Recipe");
        var availability = _tools.Locate();
        if (!availability.IsReady || string.IsNullOrWhiteSpace(availability.FfmpegPath))
            throw new InvalidOperationException(availability.Message);
        var recipe = job.TemplateVideoRecipe;
        var jobDirectory = PathSecurity.EnsureUnderRoot(
            Path.Combine(job.ProjectRoot, "jobs", job.JobId.ToString("N")), job.ProjectRoot);
        ValidateLocalRecipe(recipe, jobDirectory);
        var output = PathSecurity.EnsureUnderRoot(job.OutputPath, job.ProjectRoot);
        if (File.Exists(output)) throw new IOException("Template Video output already exists.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var partial = output + ".partial.mp4";
        TryDelete(partial);
        var scriptBefore = new FileInfo(recipe.ScriptPath);
        var arguments = BuildArguments(job, partial);
        var startInfo = new ProcessStartInfo
        {
            FileName = availability.FfmpegPath,
            WorkingDirectory = jobDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        await emit(new AgentEvent
        {
            EventType = "template_video.started",
            ProjectId = job.ProjectId,
            JobId = job.JobId,
            AgentId = AgentIds.Render,
            Action = "render_template_video",
            Status = AgentStatuses.Rendering,
            Progress = 0,
            Message = $"กำลังสร้างวิดีโอจากเอกสาร {recipe.Recipe.Sections.Count} Section",
            InputPath = recipe.ScriptPath,
            OutputPath = output
        });
        var startedAt = DateTimeOffset.UtcNow;
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidOperationException("ไม่สามารถเริ่ม FFmpeg Template Video ได้");
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
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
                    var percentage = Math.Clamp(seconds / Math.Max(0.001, job.ExpectedDurationSeconds) * 100, 0, 99.9);
                    var rounded = (int)Math.Floor(percentage);
                    if (rounded <= lastRounded) continue;
                    lastRounded = rounded;
                    await update(new JobProgress
                    {
                        JobId = job.JobId,
                        Status = JobStatuses.Exporting,
                        Progress = percentage,
                        ActiveAgentId = AgentIds.Render,
                        Message = "กำลัง Render Slide และ Voiceover",
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

        var stderr = await stderrTask;
        if (process.ExitCode != 0 || !progressEnded || !File.Exists(partial) || new FileInfo(partial).Length == 0)
        {
            TryDelete(partial);
            throw new InvalidOperationException(
                $"FFmpeg Template Video ล้มเหลว (exit {process.ExitCode}): {Tail(stderr, 20)}");
        }
        File.Move(partial, output, false);
        var scriptAfter = new FileInfo(recipe.ScriptPath);
        var sourceWasModified = scriptBefore.Length != scriptAfter.Length || scriptBefore.LastWriteTimeUtc != scriptAfter.LastWriteTimeUtc;
        await update(new JobProgress
        {
            JobId = job.JobId,
            Status = JobStatuses.Exporting,
            Progress = 100,
            EstimatedRemainingSeconds = 0,
            ActiveAgentId = AgentIds.Render,
            Message = "Template Video สำเร็จ รอตรวจ QA",
            WorkerProcessId = Environment.ProcessId
        });
        await emit(new AgentEvent
        {
            EventType = "template_video.completed",
            ProjectId = job.ProjectId,
            JobId = job.JobId,
            AgentId = AgentIds.Render,
            Action = "render_template_video",
            Status = AgentStatuses.Completed,
            Progress = 100,
            Message = recipe.VoiceoverPath is null
                ? "สร้างวิดีโอจากเอกสารสำเร็จ"
                : "สร้างวิดีโอจากเอกสารพร้อม Windows Voiceover สำเร็จ",
            InputPath = recipe.ScriptPath,
            OutputPath = output
        });
        return new ProcessingReport
        {
            JobId = job.JobId,
            InputPath = recipe.ScriptPath,
            OutputPath = output,
            Segments = job.Segments,
            Encoder = "libx264",
            AudioEncoder = recipe.VoiceoverPath is null ? "none" : "aac",
            SafeCommandDisplay = BuildSafeDisplay(arguments),
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            SourceWasModified = sourceWasModified
        };
    }

    internal static IReadOnlyList<string> BuildArguments(JobDocument job, string output)
    {
        var wrapper = job.TemplateVideoRecipe!;
        var recipe = wrapper.Recipe;
        var duration = job.ExpectedDurationSeconds.ToString("0.######", CultureInfo.InvariantCulture);
        var background = recipe.ThemeId switch
        {
            "light_clean" => "0xF1F5F9",
            "forest_green" => "0x052E16",
            "ocean_blue" => "0x082F49",
            _ => "0x0F172A"
        };
        var arguments = new List<string>
        {
            "-hide_banner", "-y",
            "-f", "lavfi", "-i",
            $"color=c={background}:s={Math.Clamp(recipe.Width, 320, 3840)}x{Math.Clamp(recipe.Height, 240, 3840)}:r={Math.Clamp(recipe.FrameRate, 15, 60)}:d={duration}"
        };
        if (wrapper.VoiceoverPath is not null)
            arguments.AddRange(["-i", wrapper.VoiceoverPath]);
        arguments.AddRange(["-vf", "ass=filename='slides.ass'"]);
        if (wrapper.VoiceoverPath is not null)
            arguments.AddRange(["-af", $"apad=pad_dur={duration}", "-c:a", "aac", "-b:a", "192k"]);
        else
            arguments.Add("-an");
        arguments.AddRange([
            "-t", duration,
            "-c:v", "libx264", "-preset", "medium", "-crf", "20", "-pix_fmt", "yuv420p",
            "-movflags", "+faststart", "-progress", "pipe:1", "-nostats", output
        ]);
        return arguments;
    }

    private static void ValidateLocalRecipe(TemplateVideoJobRecipe recipe, string jobDirectory)
    {
        foreach (var path in new[] { recipe.ScriptPath, recipe.AssPath, recipe.VoiceoverPath })
        {
            if (path is null) continue;
            if (!File.Exists(path) ||
                !Path.GetFullPath(path).StartsWith(Path.GetFullPath(jobDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Template asset is missing or outside the job directory.");
        }
        if (!string.Equals(Path.GetFileName(recipe.AssPath), "slides.ass", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Template subtitle file must be staged as slides.ass.");
    }

    private static string BuildSafeDisplay(IEnumerable<string> arguments) =>
        "ffmpeg " + string.Join(' ', arguments.Select(value => Path.IsPathRooted(value) ? "<local-template-asset>" : value));
    private static string Tail(string value, int count) => string.Join(Environment.NewLine, value.Split(['\r','\n'], StringSplitOptions.RemoveEmptyEntries).TakeLast(count));
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
}
