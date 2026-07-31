using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
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
        var sceneAssets = FindSceneAssets(jobDirectory, recipe.Recipe.Sections.Count);
        var output = PathSecurity.EnsureUnderRoot(job.OutputPath, job.ProjectRoot);
        if (File.Exists(output))
            throw new IOException("Template Video output already exists.");
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
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        await emit(new AgentEvent
        {
            EventType = "script_video.started",
            ProjectId = job.ProjectId,
            JobId = job.JobId,
            AgentId = AgentIds.Render,
            Action = "render_script_video",
            Status = AgentStatuses.Rendering,
            Progress = 0,
            Message = sceneAssets.Count == 0
                ? $"กำลังสร้าง Script Video {recipe.Recipe.Sections.Count} ฉากด้วย Motion Text"
                : $"กำลังสร้าง Script Video {recipe.Recipe.Sections.Count} ฉาก พร้อมภาพ/B-roll {sceneAssets.Count} ฉาก",
            InputPath = recipe.ScriptPath,
            OutputPath = output
        });
        var startedAt = DateTimeOffset.UtcNow;
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("ไม่สามารถเริ่ม FFmpeg Script Video ได้");
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var lastRounded = -1;
        var progressEnded = false;
        try
        {
            while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
            {
                var separator = line.IndexOf('=');
                if (separator <= 0)
                    continue;
                var key = line[..separator];
                var value = line[(separator + 1)..];
                if ((key is "out_time_us" or "out_time_ms") &&
                    long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds))
                {
                    var seconds = microseconds / 1_000_000d;
                    var percentage = Math.Clamp(seconds / Math.Max(0.001, job.ExpectedDurationSeconds) * 100, 0, 99.9);
                    var rounded = (int)Math.Floor(percentage);
                    if (rounded <= lastRounded)
                        continue;
                    lastRounded = rounded;
                    await update(new JobProgress
                    {
                        JobId = job.JobId,
                        Status = JobStatuses.Exporting,
                        Progress = percentage,
                        ActiveAgentId = AgentIds.Render,
                        Message = sceneAssets.Count == 0
                            ? "กำลัง Render ฉาก ข้อความ ซับ และเสียงบรรยาย"
                            : "กำลัง Render ภาพประกอบ ซับ และเสียงบรรยาย",
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
                process.Kill(true);
            throw;
        }

        var stderr = await stderrTask;
        if (process.ExitCode != 0 || !progressEnded || !File.Exists(partial) || new FileInfo(partial).Length == 0)
        {
            TryDelete(partial);
            throw new InvalidOperationException($"FFmpeg Script Video ล้มเหลว (exit {process.ExitCode}): {Tail(stderr, 20)}");
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
            Message = "Script Video สำเร็จ รอตรวจ QA",
            WorkerProcessId = Environment.ProcessId
        });
        await emit(new AgentEvent
        {
            EventType = "script_video.completed",
            ProjectId = job.ProjectId,
            JobId = job.JobId,
            AgentId = AgentIds.Render,
            Action = "render_script_video",
            Status = AgentStatuses.Completed,
            Progress = 100,
            Message = recipe.VoiceoverPath is null
                ? $"สร้าง Script Video สำเร็จ พร้อมภาพประกอบ {sceneAssets.Count} ฉาก"
                : $"สร้าง Script Video พร้อม Windows Voiceover และภาพประกอบ {sceneAssets.Count} ฉากสำเร็จ",
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
        var jobDirectory = Path.GetDirectoryName(wrapper.ScriptPath)
                           ?? throw new InvalidDataException("Template job directory is missing.");
        var sceneAssets = FindSceneAssets(jobDirectory, recipe.Sections.Count);
        var duration = job.ExpectedDurationSeconds.ToString("0.######", CultureInfo.InvariantCulture);
        var background = recipe.ThemeId switch
        {
            "light_clean" => "0xF1F5F9",
            "forest_green" => "0x052E16",
            "ocean_blue" => "0x082F49",
            "cinematic_visual" => "0x020617",
            _ => "0x0F172A"
        };
        var width = ClampEven(recipe.Width, 320, 3840);
        var height = ClampEven(recipe.Height, 180, 3840);
        var fps = Math.Clamp(recipe.FrameRate, 15, 60);
        var arguments = new List<string>
        {
            "-hide_banner", "-y",
            "-f", "lavfi", "-i",
            $"color=c={background}:s={width}x{height}:r={fps}:d={duration}"
        };

        foreach (var sceneAsset in sceneAssets)
        {
            if (IsImage(sceneAsset.Path))
            {
                arguments.AddRange([
                    "-loop", "1",
                    "-framerate", fps.ToString(CultureInfo.InvariantCulture),
                    "-t", FormatSeconds(sceneAsset.DurationSeconds),
                    "-i", sceneAsset.Path
                ]);
            }
            else
            {
                arguments.AddRange([
                    "-stream_loop", "-1",
                    "-t", FormatSeconds(sceneAsset.DurationSeconds),
                    "-i", sceneAsset.Path
                ]);
            }
        }

        var voiceInputIndex = -1;
        if (wrapper.VoiceoverPath is not null)
        {
            voiceInputIndex = 1 + sceneAssets.Count;
            arguments.AddRange(["-i", wrapper.VoiceoverPath]);
        }

        if (sceneAssets.Count == 0)
        {
            arguments.AddRange(["-vf", "ass=filename='slides.ass'", "-map", "0:v:0"]);
        }
        else
        {
            var filters = new List<string> { "[0:v:0]format=yuv420p[base0]" };
            var current = "base0";
            for (var index = 0; index < sceneAssets.Count; index++)
            {
                var asset = sceneAssets[index];
                var inputIndex = index + 1;
                var prepared = $"scene{index}";
                var next = $"base{index + 1}";
                var fadeOutStart = Math.Max(0, asset.DurationSeconds - 0.25);
                filters.Add(
                    $"[{inputIndex}:v:0]scale={width}:{height}:force_original_aspect_ratio=increase," +
                    $"crop={width}:{height},setsar=1,fps={fps},trim=duration={FormatSeconds(asset.DurationSeconds)}," +
                    $"setpts=PTS-STARTPTS,fade=t=in:st=0:d=0.22,fade=t=out:st={FormatSeconds(fadeOutStart)}:d=0.22," +
                    $"setpts=PTS+{FormatSeconds(asset.StartSeconds)}/TB[{prepared}]");
                filters.Add(
                    $"[{current}][{prepared}]overlay=0:0:eof_action=pass:" +
                    $"enable='between(t,{FormatSeconds(asset.StartSeconds)},{FormatSeconds(asset.EndSeconds)})'[{next}]");
                current = next;
            }
            filters.Add($"[{current}]ass=filename='slides.ass'[vout]");
            arguments.AddRange(["-filter_complex", string.Join(';', filters), "-map", "[vout]"]);
        }

        if (voiceInputIndex >= 0)
        {
            arguments.AddRange([
                "-map", $"{voiceInputIndex}:a:0",
                "-af", $"apad=pad_dur={duration}",
                "-c:a", "aac",
                "-b:a", "192k"
            ]);
        }
        else
        {
            arguments.Add("-an");
        }

        arguments.AddRange([
            "-t", duration,
            "-c:v", "libx264", "-preset", "medium", "-crf", "20", "-pix_fmt", "yuv420p",
            "-movflags", "+faststart", "-progress", "pipe:1", "-nostats", output
        ]);
        return arguments;
    }

    private static List<StagedSceneAsset> FindSceneAssets(string jobDirectory, int sectionCount)
    {
        var sections = new List<(double Start, double End)>();
        var wrapperPath = Path.Combine(jobDirectory, "document-script.json");
        TemplateVideoRecipe? recipe = null;
        try
        {
            if (File.Exists(wrapperPath))
            {
                recipe = System.Text.Json.JsonSerializer.Deserialize<TemplateVideoRecipe>(
                    File.ReadAllText(wrapperPath),
                    JsonDefaults.Options);
            }
        }
        catch
        {
            // JobDocument remains the source of truth; malformed auxiliary data is rejected later by FFmpeg/QA.
        }

        double cursor = 0;
        var effectiveSections = recipe?.Sections ?? [];
        for (var index = 0; index < sectionCount; index++)
        {
            var duration = index < effectiveSections.Count
                ? Math.Clamp(effectiveSections[index].SuggestedDurationSeconds, 2, 20)
                : 5;
            sections.Add((cursor, cursor + duration));
            cursor += duration;
        }

        var regex = new Regex(@"^scene-(?<index>\d{3})\.(jpg|jpeg|png|webp|bmp|mp4|mov|mkv|webm)$", RegexOptions.IgnoreCase);
        return Directory.EnumerateFiles(jobDirectory, "scene-*", SearchOption.TopDirectoryOnly)
            .Select(path => (Path: path, Match: regex.Match(Path.GetFileName(path))))
            .Where(item => item.Match.Success && int.TryParse(item.Match.Groups["index"].Value, out _))
            .Select(item =>
            {
                var index = int.Parse(item.Match.Groups["index"].Value, CultureInfo.InvariantCulture);
                return index >= 0 && index < sections.Count
                    ? new StagedSceneAsset(item.Path, index, sections[index].Start, sections[index].End)
                    : null;
            })
            .Where(item => item is not null)
            .Cast<StagedSceneAsset>()
            .OrderBy(item => item.SectionIndex)
            .ToList();
    }

    private static int ClampEven(int value, int minimum, int maximum)
    {
        var clamped = Math.Clamp(value, minimum, maximum);
        return clamped % 2 == 0 ? clamped : clamped - 1;
    }

    private static void ValidateLocalRecipe(TemplateVideoJobRecipe recipe, string jobDirectory)
    {
        foreach (var path in new[] { recipe.ScriptPath, recipe.AssPath, recipe.VoiceoverPath })
        {
            if (path is null)
                continue;
            if (!File.Exists(path) ||
                !Path.GetFullPath(path).StartsWith(
                    Path.GetFullPath(jobDirectory) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Template asset is missing or outside the job directory.");
        }
        if (!string.Equals(Path.GetFileName(recipe.AssPath), "slides.ass", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Template subtitle file must be staged as slides.ass.");

        foreach (var asset in Directory.EnumerateFiles(jobDirectory, "scene-*", SearchOption.TopDirectoryOnly))
        {
            if (!Path.GetFullPath(asset).StartsWith(
                    Path.GetFullPath(jobDirectory) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Scene visual is outside the job directory.");
        }
    }

    private static bool IsImage(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp";

    private static string FormatSeconds(double seconds) =>
        seconds.ToString("0.######", CultureInfo.InvariantCulture);

    private static string BuildSafeDisplay(IEnumerable<string> arguments) =>
        "ffmpeg " + string.Join(' ', arguments.Select(value => Path.IsPathRooted(value) ? "<local-script-asset>" : value));

    private static string Tail(string value, int count) => string.Join(
        Environment.NewLine,
        value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).TakeLast(count));

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private sealed record StagedSceneAsset(
        string Path,
        int SectionIndex,
        double StartSeconds,
        double EndSeconds)
    {
        public double DurationSeconds => Math.Max(0.1, EndSeconds - StartSeconds);
    }
}
