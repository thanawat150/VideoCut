using System.Text;
using System.Text.Json;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;

namespace AutoCutStudio.Infrastructure;

public sealed class TemplateVideoJobFactory
{
    private readonly JobRepository _jobs;

    public TemplateVideoJobFactory(JobRepository jobs) => _jobs = jobs;

    public async Task<JobDocument> CreateAsync(
        ProjectDocument project,
        TemplateVideoRecipe recipe,
        CancellationToken cancellationToken = default)
    {
        if (recipe.Sections.Count == 0)
            throw new InvalidDataException("Template Video ต้องมีอย่างน้อย 1 Section");
        var jobId = Guid.NewGuid();
        var directory = PathSecurity.EnsureUnderRoot(
            Path.Combine(project.RootPath, "jobs", jobId.ToString("N")), project.RootPath);
        Directory.CreateDirectory(directory);
        var scriptPath = Path.Combine(directory, "document-script.json");
        var assPath = Path.Combine(directory, "slides.ass");
        await File.WriteAllTextAsync(
            scriptPath,
            JsonSerializer.Serialize(recipe, JsonDefaults.Options),
            new UTF8Encoding(false),
            cancellationToken);
        var builder = new TemplateVideoAssBuilder();
        await File.WriteAllTextAsync(
            assPath,
            builder.Build(recipe),
            new UTF8Encoding(false),
            cancellationToken);

        string? voiceoverPath = null;
        if (recipe.GenerateWindowsVoiceover)
        {
            voiceoverPath = Path.Combine(directory, "voiceover.wav");
            var narration = string.Join(
                Environment.NewLine,
                recipe.Sections.Select(section => $"{section.Heading}. {section.Body}"));
            await new WindowsVoiceoverService().GenerateAsync(narration, voiceoverPath, cancellationToken);
        }

        var output = VersionedPathService.GetNextAvailablePath(
            Path.Combine(project.RootPath, "exports", "template-video"),
            string.IsNullOrWhiteSpace(recipe.Title) ? "document_video" : SanitizeName(recipe.Title),
            ".mp4");
        var duration = builder.TotalDuration(recipe);
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
                Recipe = recipe,
                ScriptPath = scriptPath,
                VoiceoverPath = voiceoverPath,
                AssPath = assPath
            }
        };
        await InitializeAsync(job, cancellationToken);
        return job;
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
            Message = "เพิ่ม Template Video Job เข้าคิว"
        }, cancellationToken);
        await AtomicJsonFile.WriteAsync(Path.Combine(directory, "control.json"), new JobControl(), cancellationToken);
        foreach (var name in new[] { "run.log", "error.log", "agent-events.jsonl" })
            await File.WriteAllTextAsync(Path.Combine(directory, name), string.Empty, new UTF8Encoding(false), cancellationToken);
    }

    private static string SanitizeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "document_video" : cleaned;
    }
}
