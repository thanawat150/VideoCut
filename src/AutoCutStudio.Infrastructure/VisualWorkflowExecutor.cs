using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;

namespace AutoCutStudio.Infrastructure;

public sealed record WorkflowApprovalRequest
{
    public Guid RunId { get; init; }
    public Guid NodeId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<string> Items { get; init; } = [];
}

public sealed record WorkflowExecutionResult
{
    public WorkflowRunDocument Run { get; init; } = new();
    public ProjectDocument UpdatedProject { get; init; } = new();
    public IReadOnlyList<JobDocument> CreatedJobs { get; init; } = [];
    public IReadOnlyList<BrollSuggestion> BrollSuggestions { get; init; } = [];
}

public sealed class VisualWorkflowExecutor
{
    private readonly ToolLocator _tools;
    private readonly SpeechToolLocator _speechTools;
    private readonly JobRepository _jobs;
    private readonly ProjectRepository _projects;
    private readonly VisualWorkflowRepository _workflows;
    private readonly PlatformPresetCatalog _platforms;

    public VisualWorkflowExecutor(
        ToolLocator? tools = null,
        SpeechToolLocator? speechTools = null,
        JobRepository? jobs = null,
        ProjectRepository? projects = null,
        VisualWorkflowRepository? workflows = null,
        PlatformPresetCatalog? platforms = null)
    {
        _tools = tools ?? new ToolLocator();
        _speechTools = speechTools ?? new SpeechToolLocator();
        _jobs = jobs ?? new JobRepository();
        _projects = projects ?? new ProjectRepository();
        _workflows = workflows ?? new VisualWorkflowRepository();
        _platforms = platforms ?? new PlatformPresetCatalog();
    }

    public async Task<WorkflowExecutionResult> ExecuteAsync(
        ProjectDocument project,
        VisualWorkflowDocument workflow,
        IReadOnlyList<MediaAsset>? selectedMedia = null,
        Func<WorkflowNodeRunState, Task>? nodeUpdated = null,
        Func<WorkflowApprovalRequest, Task<bool>>? requestApproval = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(workflow);
        var validation = new VisualWorkflowValidator().Validate(workflow);
        if (!validation.IsValid)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, validation.Errors));
        }

        var nodes = workflow.Nodes.ToDictionary(item => item.NodeId);
        var run = new WorkflowRunDocument
        {
            WorkflowId = workflow.WorkflowId,
            ProjectId = project.ProjectId,
            Status = WorkflowRunStatuses.Running,
            OrderedNodeIds = validation.TopologicalOrder,
            Warnings = validation.Warnings.ToList(),
            NodeStates = validation.TopologicalOrder.Select(nodeId => new WorkflowNodeRunState
            {
                NodeId = nodeId,
                NodeType = nodes[nodeId].NodeType,
                Status = WorkflowRunStatuses.Queued,
                Message = "รอทำงาน"
            }).ToList()
        };
        await _workflows.SaveAsync(project.RootPath, workflow, cancellationToken);
        await _workflows.SaveRunAsync(project.RootPath, run, cancellationToken);

        var media = (selectedMedia is { Count: > 0 } ? selectedMedia : project.SourceMedia)
            .Where(item => File.Exists(item.SourcePath))
            .DistinctBy(item => item.Id)
            .ToList();
        var activeMedia = media.LastOrDefault() ?? project.SourceMedia.LastOrDefault();
        var currentSegments = activeMedia is null
            ? new List<TimelineSegment>()
            : [new TimelineSegment { StartSeconds = 0, EndSeconds = activeMedia.Metadata.DurationSeconds }];
        TranscriptDocument? transcript = null;
        IReadOnlyList<HighlightCandidate> highlights = [];
        IReadOnlyList<BrollSuggestion> brollSuggestions = [];
        var approvedBroll = new List<BrollOverlayRecipe>();
        var createdJobs = new List<JobDocument>();
        var updatedProject = project;

        try
        {
            foreach (var nodeId in validation.TopologicalOrder)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var node = nodes[nodeId];
                var state = run.NodeStates.Single(item => item.NodeId == nodeId);
                if (node.IsDisabled)
                {
                    await CompleteNodeAsync(run, state, WorkflowRunStatuses.Skipped, "Node ถูกปิดใช้งาน", nodeUpdated, project.RootPath, cancellationToken);
                    continue;
                }

                state.Status = WorkflowRunStatuses.Running;
                state.Progress = 0;
                state.StartedAt = DateTimeOffset.UtcNow;
                state.Message = $"กำลังทำงาน: {node.DisplayName}";
                await SaveAndNotifyAsync(run, state, nodeUpdated, project.RootPath, cancellationToken);

                switch (node.NodeType)
                {
                    case VisualNodeTypes.MediaInput:
                        EnsureMedia(activeMedia);
                        state.InputSummary = activeMedia!.SourcePath;
                        state.OutputSummary = "1 media asset";
                        break;

                    case VisualNodeTypes.FolderInput:
                        if (media.Count == 0)
                        {
                            throw new InvalidOperationException("Project ไม่มีคลิปที่ใช้งานได้");
                        }
                        state.InputSummary = project.RootPath;
                        state.OutputSummary = $"{media.Count} media assets";
                        break;

                    case VisualNodeTypes.MergeClips:
                        if (media.Count < 2)
                        {
                            run.Warnings.Add("Merge Clips ต้องมีอย่างน้อย 2 คลิป ระบบจะใช้คลิปปัจจุบันแทน");
                        }
                        state.OutputSummary = $"เตรียมลำดับ {Math.Max(1, media.Count)} คลิป";
                        break;

                    case VisualNodeTypes.Transcribe:
                        EnsureMedia(activeMedia);
                        transcript = await TranscribeAsync(
                            project,
                            activeMedia!,
                            node,
                            async (progress, message) =>
                            {
                                state.Progress = progress ?? state.Progress;
                                state.Message = message;
                                await SaveAndNotifyAsync(run, state, nodeUpdated, project.RootPath, cancellationToken);
                            },
                            cancellationToken);
                        state.OutputSummary = $"Transcript {transcript.Segments.Count} ช่วง";
                        break;

                    case VisualNodeTypes.RemoveFillers:
                        EnsureMedia(activeMedia);
                        transcript ??= await new TranscriptRepository().OpenAsync(project.RootPath, cancellationToken)
                                      ?? throw new InvalidOperationException("ต้องถอดเสียงก่อนลบคำฟิลเลอร์");
                        var transcriptEditor = new TranscriptEditingService();
                        var fillerCount = transcriptEditor.MarkIsolatedFillers(transcript, excludeFromAudio: true);
                        await new TranscriptRepository().SaveAsync(project.RootPath, transcript, cancellationToken);
                        currentSegments = IntersectSegments(
                            currentSegments,
                            transcriptEditor.BuildTimelineWithoutExcluded(
                                transcript,
                                activeMedia!.Metadata.DurationSeconds).ToList());
                        state.OutputSummary = $"ตัดคำฟิลเลอร์เดี่ยว {fillerCount} ช่วง";
                        break;

                    case VisualNodeTypes.RemoveSilence:
                        EnsureMedia(activeMedia);
                        if (!activeMedia!.Metadata.HasAudio)
                        {
                            run.Warnings.Add("คลิปไม่มี Audio Stream จึงข้ามการตัดช่วงเงียบ");
                            state.OutputSummary = "ข้าม: ไม่มีเสียง";
                            break;
                        }
                        var silenceOptions = SilencePreset(GetSetting(node, "preset", "balanced"));
                        var detection = await new FfmpegSilenceDetector(_tools).DetectAsync(
                            activeMedia.SourcePath,
                            activeMedia.Metadata.DurationSeconds,
                            silenceOptions,
                            cancellationToken);
                        var silencePlan = new AutomaticEditPlanner().BuildRemoveSilencePlan(
                            "Visual Workflow",
                            activeMedia.SourcePath,
                            activeMedia.Metadata.DurationSeconds,
                            silenceOptions,
                            detection.Intervals,
                            detection.SafeCommandDisplay);
                        if (silencePlan.IsActionable)
                        {
                            currentSegments = IntersectSegments(currentSegments, silencePlan.ProposedSegments.ToList());
                        }
                        run.Warnings.AddRange(silencePlan.Warnings);
                        state.OutputSummary = $"ตัดเงียบ {silencePlan.RemovedDurationSeconds:0.0} วินาที";
                        break;

                    case VisualNodeTypes.CreateCaptions:
                        transcript ??= await new TranscriptRepository().OpenAsync(project.RootPath, cancellationToken)
                                      ?? throw new InvalidOperationException("ต้องถอดเสียงก่อนสร้าง Subtitle");
                        var srtPath = await new TranscriptRepository().ExportSrtAsync(
                            project.RootPath,
                            transcript,
                            new TranscriptEditingService(),
                            cancellationToken);
                        state.OutputPaths.Add(srtPath);
                        state.OutputSummary = "สร้าง SRT และเตรียม Animated Caption";
                        break;

                    case VisualNodeTypes.AnalyzeHighlights:
                        EnsureMedia(activeMedia);
                        transcript ??= await new TranscriptRepository().OpenAsync(project.RootPath, cancellationToken)
                                      ?? throw new InvalidOperationException("ต้องถอดเสียงก่อนวิเคราะห์ Highlight");
                        var target = GetDouble(node, "duration_seconds", 45, 10, 180);
                        var count = GetInt(node, "count", 8, 1, 20);
                        highlights = new HighlightAnalyzer().Analyze(
                            transcript,
                            activeMedia!.Metadata.DurationSeconds,
                            new HighlightAnalysisOptions
                            {
                                TargetDurationSeconds = target,
                                MinimumDurationSeconds = Math.Min(10, target),
                                MaximumDurationSeconds = Math.Max(target, Math.Min(180, target + 20)),
                                MaximumCandidates = count,
                                ContextPaddingSeconds = 0.3
                            });
                        state.OutputSummary = $"เสนอ Highlight {highlights.Count} ช่วง";
                        break;

                    case VisualNodeTypes.AutoBroll:
                        transcript ??= await new TranscriptRepository().OpenAsync(project.RootPath, cancellationToken)
                                      ?? throw new InvalidOperationException("ต้องมี Transcript ก่อนเลือก B-roll อัตโนมัติ");
                        var assetsDirectory = ResolveAssetsDirectory(project.RootPath, node);
                        Directory.CreateDirectory(assetsDirectory);
                        brollSuggestions = new BrollSuggestionService().Suggest(
                            transcript,
                            objectAnalysis: null,
                            assetsDirectory,
                            maximumSuggestions: GetInt(node, "maximum", 20, 1, 100));
                        approvedBroll = brollSuggestions
                            .Where(item => !string.IsNullOrWhiteSpace(item.MatchedLocalAsset))
                            .Select(item => new BrollOverlayRecipe
                            {
                                AssetPath = item.MatchedLocalAsset!,
                                StartSeconds = item.StartSeconds,
                                EndSeconds = Math.Max(item.StartSeconds + 1.2, item.EndSeconds),
                                Confidence = 0.86,
                                SourceKind = "project_asset",
                                Motion = "ken_burns"
                            }).ToList();
                        state.OutputSummary = $"จับคู่ B-roll ได้ {approvedBroll.Count}/{brollSuggestions.Count} ช่วง";
                        break;

                    case VisualNodeTypes.VisualApproval:
                        if (approvedBroll.Count == 0)
                        {
                            state.OutputSummary = "ไม่มีภาพประกอบที่ต้องตรวจ";
                            break;
                        }
                        state.Status = WorkflowRunStatuses.WaitingForApproval;
                        state.Message = $"รอตรวจภาพประกอบ {approvedBroll.Count} รายการ";
                        await SaveAndNotifyAsync(run, state, nodeUpdated, project.RootPath, cancellationToken);
                        var approved = requestApproval is not null && await requestApproval(new WorkflowApprovalRequest
                        {
                            RunId = run.RunId,
                            NodeId = node.NodeId,
                            Title = "ตรวจภาพประกอบอัตโนมัติ",
                            Message = "ภาพเหล่านี้จะถูกคัดลอกเข้า Job และวางตาม Timestamp ของ Transcript",
                            Items = approvedBroll.Select(item =>
                                $"{item.StartSeconds:0.0}–{item.EndSeconds:0.0}s | {Path.GetFileName(item.AssetPath)}").ToList()
                        });
                        if (!approved)
                        {
                            approvedBroll.Clear();
                            run.Warnings.Add("ผู้ใช้ยังไม่อนุมัติ B-roll ระบบจึง Export โดยไม่ใส่ภาพประกอบ");
                        }
                        state.OutputSummary = approved ? "อนุมัติภาพประกอบแล้ว" : "ไม่ใช้ภาพประกอบ";
                        break;

                    case VisualNodeTypes.EnhanceAudio:
                        state.OutputSummary = "เปิด Voice Enhancement / Noise Reduction ในขั้น Export";
                        break;

                    case VisualNodeTypes.Stabilize:
                        state.OutputSummary = "เปิด Basic Stabilization ในขั้น Export";
                        break;

                    case VisualNodeTypes.ColorCorrection:
                        state.OutputSummary = $"ใช้สี {GetSetting(node, "preset", "natural")} ในขั้น Export";
                        break;

                    case VisualNodeTypes.PlatformStyle:
                        var platform = _platforms.Get(GetSetting(node, "preset", "tiktok"));
                        state.OutputSummary = $"{platform.DisplayName} {platform.Width}×{platform.Height}";
                        break;

                    case VisualNodeTypes.CreateShorts:
                        if (highlights.Count == 0)
                        {
                            throw new InvalidOperationException("ต้องวิเคราะห์ Highlight ก่อนสร้าง Shorts");
                        }
                        state.OutputSummary = $"เลือก {Math.Min(GetInt(node, "count", 3, 1, 10), highlights.Count)} Highlight";
                        break;

                    case VisualNodeTypes.ExportVideo:
                        EnsureMedia(activeMedia);
                        var presetId = FindUpstreamSetting(
                            workflow,
                            node.NodeId,
                            VisualNodeTypes.PlatformStyle,
                            "preset") ?? GetSetting(node, "preset", "youtube_landscape");
                        var preset = _platforms.Get(presetId);
                        var needsMerge = media.Count > 1 && HasUpstreamNode(workflow, node.NodeId, VisualNodeTypes.MergeClips);
                        if (needsMerge)
                        {
                            var mergeNode = FindNearestUpstreamNode(workflow, node.NodeId, VisualNodeTypes.MergeClips);
                            var transition = mergeNode is null ? "cut" : GetSetting(mergeNode, "transition", "cut");
                            var job = await new MultiClipJobFactory(_jobs).CreateAsync(
                                updatedProject,
                                media,
                                preset,
                                transition,
                                transitionDurationSeconds: 0.35,
                                normalizeAudio: mergeNode is null || GetBool(mergeNode, "normalize_audio", true),
                                cancellationToken);
                            createdJobs.Add(job);
                        }
                        else if (highlights.Count > 0 &&
                                 (HasUpstreamNode(workflow, node.NodeId, VisualNodeTypes.CreateShorts) ||
                                  preset.Height >= preset.Width))
                        {
                            transcript ??= await new TranscriptRepository().OpenAsync(project.RootPath, cancellationToken)
                                          ?? throw new InvalidOperationException("ไม่พบ Transcript สำหรับสร้าง Shorts");
                            var shortsNode = FindNearestUpstreamNode(workflow, node.NodeId, VisualNodeTypes.CreateShorts);
                            var maximum = shortsNode is null ? 3 : GetInt(shortsNode, "count", 3, 1, 10);
                            var burnCaptions = HasUpstreamNode(workflow, node.NodeId, VisualNodeTypes.CreateCaptions);
                            var assBuilder = new AssCaptionBuilder();
                            foreach (var candidate in highlights.Take(maximum))
                            {
                                var overlays = approvedBroll
                                    .Where(item => item.EndSeconds > candidate.StartSeconds &&
                                                   item.StartSeconds < candidate.EndSeconds)
                                    .ToList();
                                var ass = assBuilder.Build(
                                    transcript,
                                    candidate,
                                    preset,
                                    GetSetting(node, "hook", string.Empty),
                                    GetSetting(node, "cta", string.Empty));
                                var job = await _jobs.CreateSocialClipJobAsync(
                                    updatedProject,
                                    activeMedia!,
                                    candidate,
                                    preset,
                                    ass,
                                    GetSetting(node, "hook", string.Empty),
                                    GetSetting(node, "cta", string.Empty),
                                    burnCaptions,
                                    overlays,
                                    priority: 10 + candidate.Rank,
                                    cancellationToken);
                                job = ApplyEnhancementSettings(job, workflow, node.NodeId);
                                await _jobs.WriteJobAsync(job, cancellationToken);
                                createdJobs.Add(job);
                            }
                        }
                        else if (HasEnhancementUpstream(workflow, node.NodeId))
                        {
                            var plan = BuildEnhancementPlan(workflow, node.NodeId);
                            var job = await new EnhancementJobFactory(_jobs).CreateAsync(
                                updatedProject,
                                activeMedia!,
                                currentSegments,
                                plan,
                                cancellationToken);
                            createdJobs.Add(job);
                        }
                        else
                        {
                            var job = await _jobs.CreateTimelineExportJobAsync(
                                updatedProject,
                                activeMedia!,
                                currentSegments,
                                cancellationToken);
                            createdJobs.Add(job);
                        }
                        state.OutputSummary = $"สร้าง Export Job รวม {createdJobs.Count} งาน";
                        state.OutputPaths.AddRange(createdJobs.Select(item => item.OutputPath));
                        break;

                    case VisualNodeTypes.Notify:
                        state.OutputSummary = "จะแจ้งเตือนเมื่อ Job ทั้งหมดเสร็จ";
                        break;

                    default:
                        throw new NotSupportedException($"ยังไม่มี Executor สำหรับ Node: {node.NodeType}");
                }

                await CompleteNodeAsync(
                    run,
                    state,
                    WorkflowRunStatuses.Completed,
                    state.OutputSummary ?? "สำเร็จ",
                    nodeUpdated,
                    project.RootPath,
                    cancellationToken);
            }

            if (createdJobs.Count > 0)
            {
                var history = updatedProject.JobHistory.ToList();
                history.AddRange(createdJobs.Select(item => item.JobId));
                updatedProject = updatedProject with { JobHistory = history };
                await _projects.SaveAsync(updatedProject, cancellationToken);
            }

            run.Status = run.Warnings.Count == 0
                ? WorkflowRunStatuses.Completed
                : WorkflowRunStatuses.CompletedWithWarnings;
            await _workflows.SaveRunAsync(project.RootPath, run, cancellationToken);
            return new WorkflowExecutionResult
            {
                Run = run,
                UpdatedProject = updatedProject,
                CreatedJobs = createdJobs,
                BrollSuggestions = brollSuggestions
            };
        }
        catch (OperationCanceledException)
        {
            run.Status = WorkflowRunStatuses.Cancelled;
            await _workflows.SaveRunAsync(project.RootPath, run, CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            run.Status = WorkflowRunStatuses.Failed;
            var activeState = run.NodeStates.FirstOrDefault(item => item.Status is
                WorkflowRunStatuses.Running or WorkflowRunStatuses.WaitingForApproval);
            if (activeState is not null)
            {
                activeState.Status = WorkflowRunStatuses.Failed;
                activeState.FailureMessage = exception.Message;
                activeState.Message = exception.Message;
                activeState.CompletedAt = DateTimeOffset.UtcNow;
            }
            await _workflows.SaveRunAsync(project.RootPath, run, CancellationToken.None);
            if (activeState is not null && nodeUpdated is not null)
            {
                await nodeUpdated(activeState);
            }
            throw;
        }
    }

    private async Task<TranscriptDocument> TranscribeAsync(
        ProjectDocument project,
        MediaAsset media,
        VisualWorkflowNode node,
        Func<double?, string, Task> progress,
        CancellationToken cancellationToken)
    {
        var repository = new TranscriptRepository();
        var existing = await repository.OpenAsync(project.RootPath, cancellationToken);
        var force = GetBool(node, "force", false);
        if (!force && existing is not null && existing.MediaAssetId == media.Id)
        {
            await progress(100, "ใช้ Transcript เดิมจาก Project Cache");
            return existing;
        }

        var service = new WhisperTranscriptionService(_tools, _speechTools);
        var result = await service.TranscribeAsync(
            media.SourcePath,
            project.RootPath,
            Path.Combine(project.RootPath, "cache"),
            project.ProjectId,
            media.Id,
            new TranscriptionOptions
            {
                Language = GetSetting(node, "language", "auto"),
                UseGpu = GetBool(node, "gpu", true),
                Threads = GetInt(node, "threads", Math.Max(2, Environment.ProcessorCount / 2), 1, 32)
            },
            progress,
            cancellationToken);
        return result.Transcript;
    }

    private static EnhancementPlan BuildEnhancementPlan(VisualWorkflowDocument workflow, Guid exportNodeId)
    {
        var audio = FindNearestUpstreamNode(workflow, exportNodeId, VisualNodeTypes.EnhanceAudio);
        var color = FindNearestUpstreamNode(workflow, exportNodeId, VisualNodeTypes.ColorCorrection);
        var stabilize = HasUpstreamNode(workflow, exportNodeId, VisualNodeTypes.Stabilize);
        var audioPreset = audio is null
            ? AudioEnhancementPresets.None
            : GetBool(audio, "noise_reduction", true) && GetBool(audio, "voice_enhancement", true)
                ? AudioEnhancementPresets.NoiseAndVoice
                : GetBool(audio, "voice_enhancement", true)
                    ? AudioEnhancementPresets.VoiceEnhance
                    : AudioEnhancementPresets.NoiseReduction;
        var colorPreset = color is null
            ? ColorPresets.None
            : GetSetting(color, "preset", ColorPresets.Natural).ToLowerInvariant() switch
            {
                ColorPresets.Vivid => ColorPresets.Vivid,
                ColorPresets.Warm => ColorPresets.Warm,
                ColorPresets.Cool => ColorPresets.Cool,
                _ => ColorPresets.Natural
            };
        return new EnhancementPlan
        {
            AudioPreset = audioPreset,
            ColorPreset = colorPreset,
            Stabilize = stabilize
        };
    }

    private static JobDocument ApplyEnhancementSettings(
        JobDocument job,
        VisualWorkflowDocument workflow,
        Guid exportNodeId)
    {
        if (job.RenderRecipe is null || !HasEnhancementUpstream(workflow, exportNodeId))
        {
            return job;
        }

        var plan = BuildEnhancementPlan(workflow, exportNodeId);
        return job with
        {
            RenderRecipe = job.RenderRecipe with
            {
                AudioEnhancementPreset = plan.AudioPreset,
                ColorPreset = plan.ColorPreset,
                Stabilize = plan.Stabilize
            }
        };
    }

    private static bool HasEnhancementUpstream(VisualWorkflowDocument workflow, Guid nodeId) =>
        HasUpstreamNode(workflow, nodeId, VisualNodeTypes.EnhanceAudio) ||
        HasUpstreamNode(workflow, nodeId, VisualNodeTypes.ColorCorrection) ||
        HasUpstreamNode(workflow, nodeId, VisualNodeTypes.Stabilize);

    private static string ResolveAssetsDirectory(string projectRoot, VisualWorkflowNode node)
    {
        var configured = GetSetting(node, "asset_directory", string.Empty);
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
        {
            return Path.GetFullPath(configured);
        }
        return Path.Combine(projectRoot, "assets", "broll");
    }

    private static List<TimelineSegment> IntersectSegments(
        IReadOnlyList<TimelineSegment> left,
        IReadOnlyList<TimelineSegment> right)
    {
        var result = new List<TimelineSegment>();
        foreach (var first in left)
        foreach (var second in right)
        {
            var start = Math.Max(first.StartSeconds, second.StartSeconds);
            var end = Math.Min(first.EndSeconds, second.EndSeconds);
            if (end > start + 0.01)
            {
                result.Add(new TimelineSegment { StartSeconds = start, EndSeconds = end });
            }
        }
        return result.OrderBy(item => item.StartSeconds).ToList();
    }

    private static SilenceDetectionOptions SilencePreset(string id) => id.ToLowerInvariant() switch
    {
        "gentle" => new SilenceDetectionOptions
        {
            PresetId = "gentle",
            DisplayName = "นุ่มนวล",
            NoiseThresholdDb = -40,
            MinimumSilenceSeconds = 0.8,
            EdgePaddingSeconds = 0.18,
            MinimumOutputSegmentSeconds = 0.1
        },
        "aggressive" => new SilenceDetectionOptions
        {
            PresetId = "aggressive",
            DisplayName = "กระชับ",
            NoiseThresholdDb = -30,
            MinimumSilenceSeconds = 0.35,
            EdgePaddingSeconds = 0.08,
            MinimumOutputSegmentSeconds = 0.06
        },
        _ => new SilenceDetectionOptions()
    };

    private static bool HasUpstreamNode(VisualWorkflowDocument workflow, Guid nodeId, string nodeType) =>
        EnumerateAncestors(workflow, nodeId).Any(item => item.NodeType == nodeType && !item.IsDisabled);

    private static VisualWorkflowNode? FindNearestUpstreamNode(
        VisualWorkflowDocument workflow,
        Guid nodeId,
        string nodeType) => EnumerateAncestors(workflow, nodeId)
        .FirstOrDefault(item => item.NodeType == nodeType && !item.IsDisabled);

    private static string? FindUpstreamSetting(
        VisualWorkflowDocument workflow,
        Guid nodeId,
        string nodeType,
        string key) => FindNearestUpstreamNode(workflow, nodeId, nodeType) is { } node
        ? GetSetting(node, key, string.Empty)
        : null;

    private static IEnumerable<VisualWorkflowNode> EnumerateAncestors(
        VisualWorkflowDocument workflow,
        Guid nodeId)
    {
        var nodes = workflow.Nodes.ToDictionary(item => item.NodeId);
        var queue = new Queue<Guid>(workflow.Connections
            .Where(item => item.TargetNodeId == nodeId)
            .Select(item => item.SourceNodeId));
        var visited = new HashSet<Guid>();
        while (queue.TryDequeue(out var current))
        {
            if (!visited.Add(current) || !nodes.TryGetValue(current, out var node))
            {
                continue;
            }
            yield return node;
            foreach (var parent in workflow.Connections
                         .Where(item => item.TargetNodeId == current)
                         .Select(item => item.SourceNodeId))
            {
                queue.Enqueue(parent);
            }
        }
    }

    private async Task CompleteNodeAsync(
        WorkflowRunDocument run,
        WorkflowNodeRunState state,
        string status,
        string message,
        Func<WorkflowNodeRunState, Task>? nodeUpdated,
        string projectRoot,
        CancellationToken cancellationToken)
    {
        state.Status = status;
        state.Progress = status == WorkflowRunStatuses.Completed ? 100 : state.Progress;
        state.Message = message;
        state.CompletedAt = DateTimeOffset.UtcNow;
        await SaveAndNotifyAsync(run, state, nodeUpdated, projectRoot, cancellationToken);
    }

    private async Task SaveAndNotifyAsync(
        WorkflowRunDocument run,
        WorkflowNodeRunState state,
        Func<WorkflowNodeRunState, Task>? nodeUpdated,
        string projectRoot,
        CancellationToken cancellationToken)
    {
        await _workflows.SaveRunAsync(projectRoot, run, cancellationToken);
        if (nodeUpdated is not null)
        {
            await nodeUpdated(state);
        }
    }

    private static void EnsureMedia(MediaAsset? media)
    {
        if (media is null || !File.Exists(media.SourcePath))
        {
            throw new InvalidOperationException("ยังไม่มี Media ที่ใช้งานได้ใน Project");
        }
    }

    private static string GetSetting(VisualWorkflowNode node, string key, string fallback) =>
        node.Settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : fallback;

    private static int GetInt(VisualWorkflowNode node, string key, int fallback, int minimum, int maximum) =>
        int.TryParse(GetSetting(node, key, fallback.ToString()), out var value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;

    private static double GetDouble(VisualWorkflowNode node, string key, double fallback, double minimum, double maximum) =>
        double.TryParse(
            GetSetting(node, key, fallback.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;

    private static bool GetBool(VisualWorkflowNode node, string key, bool fallback) =>
        bool.TryParse(GetSetting(node, key, fallback.ToString()), out var value) ? value : fallback;
}
