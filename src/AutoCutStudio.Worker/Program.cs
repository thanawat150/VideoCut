using System.Text.Json;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;
using AutoCutStudio.Infrastructure;

return await WorkerProgram.RunAsync(args);

internal static class WorkerProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Any(argument => string.Equals(argument, "--doctor", StringComparison.OrdinalIgnoreCase)))
            return RunDoctor();
        var jobPath = ParseJobPath(args);
        if (jobPath is null)
        {
            Console.Error.WriteLine("Usage: AutoCutStudio.Worker --job <path-to-job.json> | --doctor");
            return 64;
        }
        var jobs = new JobRepository();
        JobDocument job;
        try { job = await jobs.ReadJobAsync(jobPath); }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Unable to read job: {exception.Message}");
            return 65;
        }

        async Task EmitAsync(AgentEvent agentEvent)
        {
            await jobs.AppendEventAsync(job, agentEvent);
            Console.WriteLine("EVENT:" + JsonSerializer.Serialize(agentEvent, JsonDefaults.CompactOptions));
        }
        async Task UpdateProgressAsync(JobProgress progress) => await jobs.WriteProgressAsync(job, progress);

        try
        {
            var tools = new ToolLocator();
            var availability = tools.Locate();
            if (!availability.IsReady) throw new InvalidOperationException(availability.Message);
            job = job with
            {
                Status = JobStatuses.Preparing,
                WorkerProcessId = Environment.ProcessId,
                FailureMessage = null
            };
            await jobs.WriteJobAsync(job);
            await EmitAsync(new AgentEvent
            {
                EventType = "job.started",
                ProjectId = job.ProjectId,
                JobId = job.JobId,
                AgentId = AgentIds.VideoEditor,
                Action = "prepare_timeline",
                Status = AgentStatuses.Editing,
                Progress = 0,
                Message = $"Video Editor ตรวจ {job.JobType} และเตรียมงาน",
                InputPath = job.InputPath,
                OutputPath = job.OutputPath
            });
            await jobs.AppendRunLogAsync(job, $"Worker PID: {Environment.ProcessId}");
            await jobs.AppendRunLogAsync(job, $"Job Type: {job.JobType}");
            await jobs.AppendRunLogAsync(job, $"Input: {job.InputPath}");
            await jobs.AppendRunLogAsync(job, $"Output: {job.OutputPath}");
            await jobs.AppendRunLogAsync(job, $"Segments: {job.Segments.Count}");
            await EmitAsync(new AgentEvent
            {
                EventType = "agent.completed",
                ProjectId = job.ProjectId,
                JobId = job.JobId,
                AgentId = AgentIds.VideoEditor,
                Action = "prepare_timeline",
                Status = AgentStatuses.Completed,
                Progress = 100,
                Message = "Timeline และ Render Recipe พร้อมส่งให้ Render Agent",
                InputPath = job.InputPath,
                OutputPath = job.OutputPath
            });

            job = job with { Status = JobStatuses.Exporting };
            await jobs.WriteJobAsync(job);
            AutoCutStudio.Core.Interfaces.IVideoProcessor processor = job.JobType switch
            {
                JobTypes.SocialClipExport => new FfmpegSocialProcessor(tools),
                JobTypes.EnhancedExport => new FfmpegEnhancedProcessor(tools),
                AdvancedJobTypes.PrivacyBlurExport => new FfmpegPrivacyBlurProcessor(tools),
                AdvancedJobTypes.TemplateVideoExport => new FfmpegTemplateVideoProcessor(tools),
                ProfessionalJobTypes.MulticamExport or
                ProfessionalJobTypes.KeyframeExport or
                ProfessionalJobTypes.NestedSequenceExport => new FfmpegProfessionalProcessor(tools),
                _ => new FfmpegTimelineProcessor(tools)
            };
            var processingReport = await processor.ProcessAsync(job, EmitAsync, UpdateProgressAsync);
            var jobDirectory = jobs.GetJobDirectory(job);
            await WriteJsonAsync(Path.Combine(jobDirectory, "processing_report.json"), processingReport);
            await jobs.AppendRunLogAsync(job, processingReport.SafeCommandDisplay);
            if (processingReport.SourceWasModified)
                throw new InvalidDataException("Source file metadata changed during processing.");

            await EmitAsync(new AgentEvent
            {
                EventType = "qa.started",
                ProjectId = job.ProjectId,
                JobId = job.JobId,
                AgentId = AgentIds.QualityControl,
                Action = "validate_output",
                Status = AgentStatuses.Analysing,
                Progress = 0,
                Message = "Quality Control ตรวจ Output ด้วย FFprobe",
                InputPath = job.InputPath,
                OutputPath = job.OutputPath
            });
            var probe = new FfprobeMediaProbe(tools);
            var qualityControl = new OutputQualityControl(probe);
            var (qaReport, manifest) = await qualityControl.ValidateAsync(job);
            await WriteJsonAsync(Path.Combine(jobDirectory, "qa_report.json"), qaReport);
            if (!qaReport.Passed || manifest is null)
                throw new InvalidDataException(qaReport.Errors.Count == 0 ? "Output QA failed." : string.Join("; ", qaReport.Errors));
            await WriteJsonAsync(Path.Combine(jobDirectory, "manifest.json"), manifest);
            await EmitAsync(new AgentEvent
            {
                EventType = "qa.completed",
                ProjectId = job.ProjectId,
                JobId = job.JobId,
                AgentId = AgentIds.QualityControl,
                Action = "validate_output",
                Status = qaReport.Warnings.Count == 0 ? AgentStatuses.Completed : AgentStatuses.Warning,
                Progress = 100,
                Message = qaReport.Warnings.Count == 0
                    ? "Output ผ่านการตรวจคุณภาพ"
                    : $"Output ผ่านพร้อมคำเตือน: {string.Join("; ", qaReport.Warnings)}",
                InputPath = job.InputPath,
                OutputPath = job.OutputPath,
                Severity = qaReport.Warnings.Count == 0 ? "info" : "warning"
            });
            job = job with
            {
                Status = qaReport.Warnings.Count == 0 ? JobStatuses.Completed : JobStatuses.CompletedWithWarnings,
                WorkerProcessId = null
            };
            await jobs.WriteJobAsync(job);
            await UpdateProgressAsync(new JobProgress
            {
                JobId = job.JobId,
                Status = job.Status,
                Progress = 100,
                EstimatedRemainingSeconds = 0,
                ActiveAgentId = AgentIds.Render,
                Message = "Export และ QA สำเร็จ",
                WorkerProcessId = null
            });
            await EmitAsync(new AgentEvent
            {
                EventType = "render.completed",
                ProjectId = job.ProjectId,
                JobId = job.JobId,
                AgentId = AgentIds.Render,
                Action = "deliver_output",
                Status = AgentStatuses.Completed,
                Progress = 100,
                Message = "Render Agent ส่งมอบ Output จริง",
                InputPath = job.InputPath,
                OutputPath = job.OutputPath,
                Metadata = new Dictionary<string, string>
                {
                    ["sha256"] = manifest.Sha256,
                    ["file_size_bytes"] = manifest.FileSizeBytes.ToString(),
                    ["job_type"] = job.JobType
                }
            });
            return 0;
        }
        catch (JobCancelledException exception)
        {
            job = job with { Status = JobStatuses.Cancelled, WorkerProcessId = null, FailureMessage = exception.Message };
            await jobs.WriteJobAsync(job);
            await jobs.WriteErrorAsync(job, exception.ToString());
            await UpdateProgressAsync(new JobProgress
            {
                JobId = job.JobId,
                Status = JobStatuses.Cancelled,
                Progress = 0,
                ActiveAgentId = AgentIds.Render,
                Message = "งานถูกยกเลิก",
                WorkerProcessId = null
            });
            await EmitAsync(new AgentEvent
            {
                EventType = "agent.cancelled",
                ProjectId = job.ProjectId,
                JobId = job.JobId,
                AgentId = AgentIds.Render,
                Action = "export_job",
                Status = AgentStatuses.Cancelled,
                Message = "ผู้ใช้ยกเลิกงาน",
                InputPath = job.InputPath,
                OutputPath = job.OutputPath,
                Severity = "warning"
            });
            return 2;
        }
        catch (Exception exception)
        {
            job = job with { Status = JobStatuses.Failed, WorkerProcessId = null, FailureMessage = exception.Message };
            try
            {
                await jobs.WriteJobAsync(job);
                await jobs.WriteErrorAsync(job, exception.ToString());
                await UpdateProgressAsync(new JobProgress
                {
                    JobId = job.JobId,
                    Status = JobStatuses.Failed,
                    Progress = 0,
                    ActiveAgentId = AgentIds.Render,
                    Message = exception.Message,
                    WorkerProcessId = null
                });
                await EmitAsync(new AgentEvent
                {
                    EventType = "job.failed",
                    ProjectId = job.ProjectId,
                    JobId = job.JobId,
                    AgentId = AgentIds.Render,
                    Action = job.JobType,
                    Status = AgentStatuses.Error,
                    Message = exception.Message,
                    InputPath = job.InputPath,
                    OutputPath = job.OutputPath,
                    Severity = "error",
                    RequiresUserAction = true
                });
            }
            catch (Exception persistenceException)
            {
                Console.Error.WriteLine($"Unable to persist failure: {persistenceException.Message}");
            }
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int RunDoctor()
    {
        var availability = new ToolLocator().Locate();
        var vision = new ComputerVisionToolLocator().Locate();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            status = availability.Status,
            ready = availability.IsReady,
            ffmpeg_path = availability.FfmpegPath,
            ffprobe_path = availability.FfprobePath,
            message = availability.Message,
            face_model_ready = vision.FaceModelPath is not null,
            object_model_ready = vision.ObjectModelPath is not null,
            vision_message = vision.Message,
            professional_jobs = new[]
            {
                ProfessionalJobTypes.MulticamExport,
                ProfessionalJobTypes.KeyframeExport,
                ProfessionalJobTypes.NestedSequenceExport
            },
            base_directory = AppContext.BaseDirectory
        }, JsonDefaults.Options));
        return availability.IsReady ? 0 : 69;
    }

    private static string? ParseJobPath(string[] args)
    {
        for (var index = 0; index < args.Length - 1; index++)
            if (string.Equals(args[index], "--job", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(args[index + 1]);
        return null;
    }

    private static Task WriteJsonAsync<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonDefaults.Options));
    }
}
