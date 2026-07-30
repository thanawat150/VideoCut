using AutoCutStudio.Core.Models;

namespace AutoCutStudio.Core.Interfaces;

public interface IAgentEventBus
{
    event EventHandler<AgentEvent>? Published;
    void Publish(AgentEvent agentEvent);
}

public interface IMediaProbe
{
    Task<MediaMetadata> ProbeAsync(string sourcePath, CancellationToken cancellationToken = default);
}

public interface IProjectRepository
{
    Task<ProjectDocument> CreateAsync(string parentDirectory, string displayName, CancellationToken cancellationToken = default);
    Task<ProjectDocument> OpenAsync(string projectFilePath, CancellationToken cancellationToken = default);
    Task SaveAsync(ProjectDocument project, CancellationToken cancellationToken = default);
}

public interface IJobRepository
{
    Task<JobDocument> CreateTimelineExportJobAsync(
        ProjectDocument project,
        MediaAsset media,
        IReadOnlyList<TimelineSegment> segments,
        CancellationToken cancellationToken = default);

    Task<JobDocument> ReadJobAsync(string jobFilePath, CancellationToken cancellationToken = default);
    Task WriteJobAsync(JobDocument job, CancellationToken cancellationToken = default);
    Task WriteProgressAsync(JobDocument job, JobProgress progress, CancellationToken cancellationToken = default);
    Task AppendEventAsync(JobDocument job, AgentEvent agentEvent, CancellationToken cancellationToken = default);
    Task AppendRunLogAsync(JobDocument job, string line, CancellationToken cancellationToken = default);
    Task WriteErrorAsync(JobDocument job, string content, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JobDocument>> ListJobsAsync(ProjectDocument project, CancellationToken cancellationToken = default);
    string GetJobDirectory(JobDocument job);
}

public interface IVideoProcessor
{
    Task<ProcessingReport> ProcessAsync(
        JobDocument job,
        Func<AgentEvent, Task> emitEventAsync,
        Func<JobProgress, Task> updateProgressAsync,
        CancellationToken cancellationToken = default);
}

public interface IOutputQualityControl
{
    Task<(QaReport Report, ExportManifest? Manifest)> ValidateAsync(
        JobDocument job,
        CancellationToken cancellationToken = default);
}

public sealed record ToolAvailability(
    bool IsReady,
    string? FfmpegPath,
    string? FfprobePath,
    string Status,
    string Message);

public interface IToolLocator
{
    ToolAvailability Locate();
}
