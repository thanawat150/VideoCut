using System.Text;
using System.Text.Json;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;

namespace AutoCutStudio.Infrastructure;

public sealed class TemplateVideoJobFactory
{
    private readonly JobRepository _jobs;

    public TemplateVideoJobFactory(JobRepository jobs) => _jobs = jobs;

    public Task<JobDocument> CreateAsync(
        ProjectDocument project,
        TemplateVideoRecipe recipe,
        CancellationToken cancellationToken = default) =>
        CreateScriptVideoAsync(
            project,
            recipe,
            sceneAssets: null,
            voiceoverOptions: null,
            pronunciationDictionary: null,
            cancellationToken);

    public Task<JobDocument> CreateScriptVideoAsync(
        ProjectDocument project,
        TemplateVideoRecipe recipe,
        IReadOnlyDictionary<int, string>? sceneAssets,
        WindowsVoiceoverOptions? voiceoverOptions,
        string? pronunciationDictionary,
        CancellationToken cancellationToken = default)
    {
        ISpeechSynthesisProvider? provider = recipe.GenerateWindowsVoiceover
            ? new WindowsSpeechSynthesisProvider()
            : null;
        SpeechSynthesisRequest? request = provider is null
            ? null
            : new SpeechSynthesisRequest
            {
                Language = voiceoverOptions?.Language ?? recipe.VoiceLanguage,
                Voice = voiceoverOptions?.PreferredVoiceName ?? "auto",
                Rate = voiceoverOptions?.Rate ?? 0,
                Volume = voiceoverOptions?.Volume ?? 100,
                Speed = 1.0
            };
        return CreateScriptVideoWithProviderAsync(
            project,
            recipe,
            sceneAssets,
            provider,
            request,
            pronunciationDictionary,
            cancellationToken);
    }

    public async Task<JobDocument> CreateScriptVideoWithProviderAsync(
        ProjectDocument project,
        TemplateVideoRecipe recipe,
        IReadOnlyDictionary<int, string>? sceneAssets,
        ISpeechSynthesisProvider? speechProvider,
        SpeechSynthesisRequest? speechRequest,
        string? pronunciationDictionary,
        CancellationToken cancellationToken = default)
    {
        if (recipe.Sections.Count == 0)
            throw new InvalidDataException("Template Video ต้องมีอย่างน้อย 1 Section");

        var jobId = Guid.NewGuid();
        var directory = PathSecurity.EnsureUnderRoot(
            Path.Combine(project.RootPath, "jobs", jobId.ToString("N")), project.RootPath);
        Directory.CreateDirectory(directory);

        var stagedRecipe = recipe with
        {
            Sections = recipe.Sections
                .OrderBy(section => section.Index)
                .Select((section, index) => section with { Index = index })
                .ToList()
        };

        if (sceneAssets is not null)
            await StageSceneAssetsAsync(project.RootPath, directory, sceneAssets, stagedRecipe.Sections.Count, cancellationToken);

        string? voiceoverPath = null;
        if (speechProvider is not null)
        {
            var availability = await speechProvider.GetAvailabilityAsync(cancellationToken);
            if (!availability.IsReady)
                throw new InvalidOperationException(availability.Message);

            voiceoverPath = Path.Combine(directory, "voiceover.wav");
            var narration = string.Join(
                Environment.NewLine,
                stagedRecipe.Sections.Select(section => $"{section.Heading}. {section.Body}"));
            narration = new ScriptVideoPlanner().ApplyPronunciationDictionary(narration, pronunciationDictionary);
            var effectiveRequest = (speechRequest ?? new SpeechSynthesisRequest()) with
            {
                Text = narration,
                OutputPath = voiceoverPath,
                Language = string.IsNullOrWhiteSpace(speechRequest?.Language)
                    ? stagedRecipe.VoiceLanguage
                    : speechRequest!.Language
            };
            await speechProvider.GenerateAsync(effectiveRequest, cancellationToken);

            try
            {
                var metadata = await new FfprobeMediaProbe(new ToolLocator()).ProbeAsync(voiceoverPath, cancellationToken);
                if (metadata.DurationSeconds > 0.5)
                    stagedRecipe = stagedRecipe with
                    {
                        Sections = RebalanceDurations(stagedRecipe.Sections, metadata.DurationSeconds + 0.6)
                    };
            }
            catch
            {
                // A valid voiceover was already generated. Estimated scene timing remains usable if probing fails.
            }
        }

        var scriptPath = Path.Combine(directory, "document-script.json");
        var assPath = Path.Combine(directory, "slides.ass");
        await File.WriteAllTextAsync(
            scriptPath,
            JsonSerializer.Serialize(stagedRecipe, JsonDefaults.Options),
            new UTF8Encoding(false),
            cancellationToken);
        var builder = new TemplateVideoAssBuilder();
        await File.WriteAllTextAsync(
            assPath,
            builder.Build(stagedRecipe),
            new UTF8Encoding(false),
            cancellationToken);

        var output = VersionedPathService.GetNextAvailablePath(
            Path.Combine(project.RootPath, "exports", "script-video"),
            string.IsNullOrWhiteSpace(stagedRecipe.Title) ? "script_video" : SanitizeName(stagedRecipe.Title),
            ".mp4");
        var duration = builder.TotalDuration(stagedRecipe);
        var job = new JobDocument
        {
            JobId = jobId,
            ProjectId = project.ProjectId,
            ProjectRoot = project.RootPath,
            JobType = AdvancedJobTypes.TemplateVideoExport,
            Status = JobStatuses.Queued,
            Priority = 30,
            InputPath = scriptPath,
            OutputPath = PathSecurity.EnsureUnderRoot(output, project.RootPath),
            Segments = [new TimelineSegment { StartSeconds = 0, EndSeconds = duration }],
            ExpectedInputHasAudio = voiceoverPath is not null,
            ExpectedDurationSeconds = duration,
            TemplateVideoRecipe = new TemplateVideoJobRecipe
            {
                Recipe = stagedRecipe,
                ScriptPath = scriptPath,
                VoiceoverPath = voiceoverPath,
                AssPath = assPath
            }
        };
        await InitializeAsync(job, cancellationToken);
        return job;
    }

    private static async Task StageSceneAssetsAsync(
        string projectRoot,
        string jobDirectory,
        IReadOnlyDictionary<int, string> sceneAssets,
        int sectionCount,
        CancellationToken cancellationToken)
    {
        foreach (var pair in sceneAssets.OrderBy(item => item.Key))
        {
            if (pair.Key < 0 || pair.Key >= sectionCount || string.IsNullOrWhiteSpace(pair.Value))
                continue;
            var source = Path.GetFullPath(pair.Value);
            if (!File.Exists(source))
                continue;

            var extension = Path.GetExtension(source).ToLowerInvariant();
            if (extension is not (".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp" or ".mp4" or ".mov" or ".mkv" or ".webm"))
                continue;

            var target = PathSecurity.EnsureUnderRoot(
                Path.Combine(jobDirectory, $"scene-{pair.Key:000}{extension}"),
                projectRoot);
            await using var input = File.OpenRead(source);
            await using var output = File.Create(target);
            await input.CopyToAsync(output, cancellationToken);
        }
    }

    private static List<DocumentSection> RebalanceDurations(
        IReadOnlyList<DocumentSection> sections,
        double targetDurationSeconds)
    {
        var minimumTotal = sections.Count * 2.0;
        var target = Math.Max(minimumTotal, targetDurationSeconds);
        var weights = sections.Select(section => Math.Max(8, section.Heading.Length + section.Body.Length)).ToArray();
        var totalWeight = Math.Max(1, weights.Sum());
        var durations = weights.Select(weight => Math.Clamp(target * weight / totalWeight, 2.0, 20.0)).ToArray();
        var actual = durations.Sum();
        if (actual > 0 && target > actual)
        {
            var remaining = target - actual;
            for (var index = 0; index < durations.Length && remaining > 0.01; index++)
            {
                var add = Math.Min(20.0 - durations[index], remaining / Math.Max(1, durations.Length - index));
                durations[index] += Math.Max(0, add);
                remaining -= Math.Max(0, add);
            }
        }

        return sections.Select((section, index) => section with
        {
            Index = index,
            SuggestedDurationSeconds = Math.Round(durations[index], 3)
        }).ToList();
    }

    private async Task InitializeAsync(JobDocument job, CancellationToken cancellationToken)
    {
        var directory = _jobs.GetJobDirectory(job);
        await _jobs.WriteJobAsync(job, cancellationToken);
        await _jobs.WriteProgressAsync(job, new JobProgress
        {
            JobId = job.JobId,
            Status = JobStatuses.Queued,
            ActiveAgentId = AgentIds.Producer,
            Message = "เพิ่ม Script-to-Video Job เข้าคิว"
        }, cancellationToken);
        await AtomicJsonFile.WriteAsync(Path.Combine(directory, "control.json"), new JobControl(), cancellationToken);
        foreach (var name in new[] { "run.log", "error.log", "agent-events.jsonl" })
            await File.WriteAllTextAsync(Path.Combine(directory, name), string.Empty, new UTF8Encoding(false), cancellationToken);
    }

    private static string SanitizeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "script_video" : cleaned;
    }
}
