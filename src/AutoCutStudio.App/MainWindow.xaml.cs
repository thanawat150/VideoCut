using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;
using AutoCutStudio.Infrastructure;
using Microsoft.Win32;

namespace AutoCutStudio.App;

public partial class MainWindow : Window
{
    private readonly AgentEventBus _eventBus = new();
    private readonly ToolLocator _toolLocator = new();
    private readonly ProjectRepository _projectRepository = new();
    private readonly JobRepository _jobRepository = new();
    private readonly ObservableCollection<TimelineSegment> _segments = [];
    private readonly Stack<List<TimelineSegment>> _undo = new();
    private readonly Stack<List<TimelineSegment>> _redo = new();
    private readonly Dictionary<string, long> _eventOffsets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, DateTimeOffset> _recentlyLaunched = [];
    private readonly DispatcherTimer _previewTimer;
    private readonly DispatcherTimer _backendTimer;

    private ProjectDocument? _project;
    private MediaAsset? _activeMedia;
    private double _inPoint;
    private double _outPoint;
    private bool _updatingSlider;
    private bool _backendRefreshRunning;
    private ToolAvailability _toolAvailability = new(false, null, null, "missing", "ยังไม่ได้ตรวจสอบ");
    private Dictionary<string, AgentUiRefs> _agentUi = [];

    public MainWindow()
    {
        InitializeComponent();
        AppPaths.EnsureCreated();

        TimelineGrid.ItemsSource = _segments;
        _eventBus.Published += EventBus_Published;

        _previewTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _previewTimer.Tick += PreviewTimer_Tick;
        _previewTimer.Start();

        _backendTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(600)
        };
        _backendTimer.Tick += BackendTimer_Tick;
        _backendTimer.Start();

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _agentUi = new Dictionary<string, AgentUiRefs>
        {
            [AgentIds.Producer] = new(ProducerAvatar, ProducerStatus, ProducerProgress, ProducerMessage),
            [AgentIds.MediaAnalyst] = new(MediaAnalystAvatar, MediaAnalystStatus, MediaAnalystProgress, MediaAnalystMessage),
            [AgentIds.VideoEditor] = new(VideoEditorAvatar, VideoEditorStatus, VideoEditorProgress, VideoEditorMessage),
            [AgentIds.QualityControl] = new(QualityControlAvatar, QualityControlStatus, QualityControlProgress, QualityControlMessage),
            [AgentIds.Render] = new(RenderAvatar, RenderStatus, RenderProgress, RenderMessage)
        };

        RefreshToolStatus();

        var recentProjectPath = Path.Combine(AppPaths.SettingsDirectory, "last-project.txt");
        if (File.Exists(recentProjectPath))
        {
            try
            {
                var path = (await File.ReadAllTextAsync(recentProjectPath)).Trim();
                if (File.Exists(path))
                {
                    await LoadProjectAsync(path);
                }
            }
            catch (Exception exception)
            {
                StatusBarText.Text = $"เปิด Project ล่าสุดไม่ได้: {exception.Message}";
            }
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _previewTimer.Stop();
        _backendTimer.Stop();
        PreviewPlayer.Stop();
    }

    private void RefreshToolStatus()
    {
        _toolAvailability = _toolLocator.Locate();
        ToolStatusText.Text =
            $"{_toolAvailability.Status}\n{_toolAvailability.Message}\n\nFFmpeg: {_toolAvailability.FfmpegPath ?? "ไม่พบ"}\nFFprobe: {_toolAvailability.FfprobePath ?? "ไม่พบ"}";
        ToolHeaderText.Text = _toolAvailability.Message;
        ToolHeaderText.Foreground = _toolAvailability.IsReady ? Brushes.LightGreen : Brushes.Orange;
    }

    private async void CreateProject_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFolderDialog
            {
                Title = "เลือกโฟลเดอร์สำหรับสร้าง Project",
                Multiselect = false
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            _project = await _projectRepository.CreateAsync(
                dialog.FolderName,
                ProjectNameTextBox.Text);
            _activeMedia = null;
            _segments.Clear();
            _undo.Clear();
            _redo.Clear();
            UpdateProjectUi();
            await SaveRecentProjectPathAsync();
            await PublishProjectEventAsync(new AgentEvent
            {
                EventType = "project.created",
                ProjectId = _project.ProjectId,
                AgentId = AgentIds.Producer,
                Action = "create_project",
                Status = AgentStatuses.Completed,
                Progress = 100,
                Message = "สร้าง Project และโครงสร้างโฟลเดอร์แล้ว",
                OutputPath = _project.RootPath
            });
            StatusBarText.Text = "สร้าง Project แล้ว";
        }
        catch (Exception exception)
        {
            ShowError("สร้าง Project ไม่สำเร็จ", exception);
        }
    }

    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "เปิด AutoCut Studio Project",
            Filter = "AutoCut project (project.json)|project.json|JSON files (*.json)|*.json"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await LoadProjectAsync(dialog.FileName);
        }
        catch (Exception exception)
        {
            ShowError("เปิด Project ไม่สำเร็จ", exception);
        }
    }

    private async Task LoadProjectAsync(string projectFilePath)
    {
        _project = await _projectRepository.OpenAsync(projectFilePath);
        _activeMedia = _project.SourceMedia.LastOrDefault();

        _segments.Clear();
        foreach (var segment in _project.Timeline.Segments)
        {
            _segments.Add(CloneSegment(segment));
        }

        _undo.Clear();
        _redo.Clear();

        if (_activeMedia is not null)
        {
            ConfigureActiveMedia(_activeMedia);
        }

        UpdateProjectUi();
        await SaveRecentProjectPathAsync();
        await RefreshJobsAsync();
        StatusBarText.Text = "เปิด Project แล้ว";
    }

    private async void ImportMp4_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            MessageBox.Show("กรุณาสร้างหรือเปิด Project ก่อน", "AutoCut Studio");
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "เลือก MP4",
            Filter = "MP4 video (*.mp4)|*.mp4",
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var sourcePath = dialog.FileName;

        try
        {
            PathSecurity.ValidateMp4Source(sourcePath);
            await PublishProjectEventAsync(new AgentEvent
            {
                EventType = "ffprobe.started",
                ProjectId = _project.ProjectId,
                AgentId = AgentIds.MediaAnalyst,
                Action = "probe_media",
                Status = AgentStatuses.Reading,
                Progress = 0,
                Message = "Media Analyst กำลังอ่าน Metadata ด้วย FFprobe",
                InputPath = sourcePath
            });

            var probe = new FfprobeMediaProbe(_toolLocator);
            var metadata = await probe.ProbeAsync(sourcePath);
            if (!metadata.HasVideo)
            {
                throw new InvalidDataException("ไฟล์นี้ไม่มี Video Stream");
            }

            var asset = new MediaAsset
            {
                Id = Guid.NewGuid(),
                SourcePath = sourcePath,
                Metadata = metadata,
                ImportedAt = DateTimeOffset.UtcNow
            };

            var media = _project.SourceMedia.ToList();
            media.Add(asset);

            _activeMedia = asset;
            _segments.Clear();
            _segments.Add(new TimelineSegment
            {
                StartSeconds = 0,
                EndSeconds = metadata.DurationSeconds
            });
            _inPoint = 0;
            _outPoint = metadata.DurationSeconds;

            _project = _project with
            {
                SourceMedia = media,
                Timeline = BuildTimelineDocument()
            };
            await _projectRepository.SaveAsync(_project);
            ConfigureActiveMedia(asset);
            UpdateProjectUi();

            await PublishProjectEventAsync(new AgentEvent
            {
                EventType = "ffprobe.completed",
                ProjectId = _project.ProjectId,
                AgentId = AgentIds.MediaAnalyst,
                Action = "probe_media",
                Status = AgentStatuses.Completed,
                Progress = 100,
                Message = $"ตรวจไฟล์สำเร็จ: {metadata.Width}×{metadata.Height}, {metadata.DurationSeconds:0.###} วินาที",
                InputPath = sourcePath
            });

            StatusBarText.Text = "Import MP4 และตรวจด้วย FFprobe แล้ว";
            MainTabs.SelectedIndex = 1;
        }
        catch (Exception exception)
        {
            await PublishProjectEventAsync(new AgentEvent
            {
                EventType = "agent.failed",
                ProjectId = _project.ProjectId,
                AgentId = AgentIds.MediaAnalyst,
                Action = "probe_media",
                Status = AgentStatuses.Error,
                Message = exception.Message,
                InputPath = sourcePath,
                Severity = "error",
                RequiresUserAction = true
            });
            ShowError("Import MP4 ไม่สำเร็จ", exception);
        }
    }

    private void ConfigureActiveMedia(MediaAsset media)
    {
        TimelineSlider.Minimum = 0;
        TimelineSlider.Maximum = Math.Max(0.001, media.Metadata.DurationSeconds);
        TimelineSlider.Value = 0;
        _inPoint = 0;
        _outPoint = media.Metadata.DurationSeconds;

        PreviewPlayer.Stop();
        PreviewPlayer.Source = new Uri(media.SourcePath, UriKind.Absolute);
        PreviewPlayer.Position = TimeSpan.Zero;

        var details = BuildMetadataText(media.Metadata);
        MediaMetadataTextBox.Text = details;
        EditorMetadataTextBox.Text = details;
        SelectionSummaryText.Text = BuildSelectionSummary();
    }

    private void UpdateProjectUi()
    {
        if (_project is null)
        {
            ProjectHeaderText.Text = "ยังไม่ได้เปิด Project";
            ProjectPathTextBox.Text = string.Empty;
            return;
        }

        ProjectHeaderText.Text = _project.DisplayName;
        ProjectPathTextBox.Text = _project.RootPath;
        ProjectNameTextBox.Text = _project.DisplayName;

        if (_activeMedia is not null)
        {
            var details = BuildMetadataText(_activeMedia.Metadata);
            MediaMetadataTextBox.Text = details;
            EditorMetadataTextBox.Text = details;
        }
    }

    private static string BuildMetadataText(MediaMetadata metadata)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Path: {metadata.SourcePath}");
        builder.AppendLine($"Container: {metadata.Container}");
        builder.AppendLine($"Video: {metadata.VideoCodec ?? "ไม่มี"}");
        builder.AppendLine($"Audio: {metadata.AudioCodec ?? "ไม่มี"}");
        builder.AppendLine($"Resolution: {metadata.Width} × {metadata.Height}");
        builder.AppendLine($"FPS: {metadata.FrameRate:0.###}");
        builder.AppendLine($"Duration: {metadata.DurationSeconds:0.###} s");
        builder.AppendLine($"Bitrate: {metadata.BitRate?.ToString() ?? "ไม่ระบุ"}");
        builder.AppendLine($"Pixel format: {metadata.PixelFormat ?? "ไม่ระบุ"}");
        builder.AppendLine($"Color space: {metadata.ColorSpace ?? "ไม่ระบุ"}");
        builder.AppendLine($"Rotation: {metadata.Rotation}");
        builder.AppendLine($"VFR: {(metadata.IsVariableFrameRate ? "ใช่" : "ไม่")}");
        builder.AppendLine($"File size: {metadata.FileSizeBytes:N0} bytes");
        builder.AppendLine();
        builder.AppendLine("Phase 1 จะอ้างอิง Source แบบอ่านอย่างเดียวและไม่แก้ไขไฟล์ต้นฉบับ");
        return builder.ToString();
    }

    private void OpenEditor_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 1;

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (PreviewPlayer.Source is null)
        {
            MessageBox.Show("ยังไม่มีวิดีโอสำหรับ Preview", "AutoCut Studio");
            return;
        }

        PreviewPlayer.Play();
    }

    private void PausePreview_Click(object sender, RoutedEventArgs e) => PreviewPlayer.Pause();

    private void PreviewPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (PreviewPlayer.NaturalDuration.HasTimeSpan)
        {
            TimelineSlider.Maximum = PreviewPlayer.NaturalDuration.TimeSpan.TotalSeconds;
        }
    }

    private void PreviewPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        StatusBarText.Text = $"Preview เปิดไม่ได้ด้วย Windows codec: {e.ErrorException.Message}";
    }

    private void PreviewTimer_Tick(object? sender, EventArgs e)
    {
        if (PreviewPlayer.Source is null)
        {
            return;
        }

        _updatingSlider = true;
        TimelineSlider.Value = Math.Clamp(
            PreviewPlayer.Position.TotalSeconds,
            TimelineSlider.Minimum,
            TimelineSlider.Maximum);
        _updatingSlider = false;
        SelectionSummaryText.Text = BuildSelectionSummary();
    }

    private void TimelineSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingSlider || PreviewPlayer.Source is null)
        {
            return;
        }

        if (Mouse.LeftButton == MouseButtonState.Pressed)
        {
            PreviewPlayer.Position = TimeSpan.FromSeconds(TimelineSlider.Value);
        }

        SelectionSummaryText.Text = BuildSelectionSummary();
    }

    private void SetIn_Click(object sender, RoutedEventArgs e)
    {
        _inPoint = TimelineSlider.Value;
        if (_outPoint <= _inPoint)
        {
            _outPoint = Math.Min(TimelineSlider.Maximum, _inPoint + 0.1);
        }

        SelectionSummaryText.Text = BuildSelectionSummary();
    }

    private void SetOut_Click(object sender, RoutedEventArgs e)
    {
        _outPoint = TimelineSlider.Value;
        if (_outPoint <= _inPoint)
        {
            _inPoint = Math.Max(0, _outPoint - 0.1);
        }

        SelectionSummaryText.Text = BuildSelectionSummary();
    }

    private async void ApplyRange_Click(object sender, RoutedEventArgs e)
    {
        if (_activeMedia is null)
        {
            return;
        }

        if (_outPoint <= _inPoint)
        {
            MessageBox.Show("Out ต้องมากกว่า In", "AutoCut Studio");
            return;
        }

        PushUndo();
        _segments.Clear();
        _segments.Add(new TimelineSegment
        {
            StartSeconds = _inPoint,
            EndSeconds = _outPoint
        });
        await AutoSaveTimelineAsync();
    }

    private async void Split_Click(object sender, RoutedEventArgs e)
    {
        if (_activeMedia is null)
        {
            return;
        }

        var position = TimelineSlider.Value;
        var segment = _segments.FirstOrDefault(item =>
            position > item.StartSeconds + 0.01 &&
            position < item.EndSeconds - 0.01);

        if (segment is null)
        {
            MessageBox.Show("Playhead ต้องอยู่ภายใน Segment", "AutoCut Studio");
            return;
        }

        PushUndo();
        var index = _segments.IndexOf(segment);
        _segments.RemoveAt(index);
        _segments.Insert(index, new TimelineSegment
        {
            StartSeconds = segment.StartSeconds,
            EndSeconds = position
        });
        _segments.Insert(index + 1, new TimelineSegment
        {
            StartSeconds = position,
            EndSeconds = segment.EndSeconds
        });
        await AutoSaveTimelineAsync();
    }

    private async void DeleteSegment_Click(object sender, RoutedEventArgs e)
    {
        if (TimelineGrid.SelectedItem is not TimelineSegment segment)
        {
            MessageBox.Show("เลือก Segment ที่ต้องการลบ", "AutoCut Studio");
            return;
        }

        PushUndo();
        _segments.Remove(segment);
        await AutoSaveTimelineAsync();
    }

    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_undo.Count == 0)
        {
            return;
        }

        _redo.Push(CloneSegments(_segments));
        RestoreSegments(_undo.Pop());
        await AutoSaveTimelineAsync();
    }

    private async void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (_redo.Count == 0)
        {
            return;
        }

        _undo.Push(CloneSegments(_segments));
        RestoreSegments(_redo.Pop());
        await AutoSaveTimelineAsync();
    }

    private void PushUndo()
    {
        _undo.Push(CloneSegments(_segments));
        _redo.Clear();
    }

    private void RestoreSegments(IEnumerable<TimelineSegment> segments)
    {
        _segments.Clear();
        foreach (var segment in segments)
        {
            _segments.Add(CloneSegment(segment));
        }
    }

    private async Task AutoSaveTimelineAsync()
    {
        if (_project is null || _activeMedia is null)
        {
            return;
        }

        _project = _project with
        {
            Timeline = BuildTimelineDocument()
        };
        await _projectRepository.SaveAsync(_project);
        StatusBarText.Text = $"Auto Save Timeline แล้ว — Output {BuildTimelineDocument().OutputDurationSeconds:0.###} วินาที";
    }

    private TimelineDocument BuildTimelineDocument() => new()
    {
        SourceMediaId = _activeMedia?.Id,
        Segments = CloneSegments(_segments),
        ModifiedAt = DateTimeOffset.UtcNow
    };

    private async void CreateExportJob_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null || _activeMedia is null)
        {
            MessageBox.Show("กรุณาเปิด Project และ Import MP4 ก่อน", "AutoCut Studio");
            return;
        }

        RefreshToolStatus();
        if (!_toolAvailability.IsReady)
        {
            MessageBox.Show(_toolAvailability.Message, "Dependency ไม่พร้อม", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            PathSecurity.ValidateTimelineSegments(_segments, _activeMedia.Metadata.DurationSeconds);
            var job = await _jobRepository.CreateTimelineExportJobAsync(
                _project,
                _activeMedia,
                _segments.ToList());

            var history = _project.JobHistory.ToList();
            history.Add(job.JobId);
            _project = _project with { JobHistory = history };
            await _projectRepository.SaveAsync(_project);

            var createdEvent = new AgentEvent
            {
                EventType = "job.created",
                ProjectId = _project.ProjectId,
                JobId = job.JobId,
                AgentId = AgentIds.Producer,
                Action = "create_timeline_export",
                Status = AgentStatuses.Completed,
                Progress = 100,
                Message = "Producer สร้าง Workflow: Video Editor → Render → Quality Control",
                InputPath = job.InputPath,
                OutputPath = job.OutputPath
            };
            await _jobRepository.AppendEventAsync(job, createdEvent);
            _eventBus.Publish(createdEvent);

            StatusBarText.Text = $"สร้าง Job {job.JobId:N} แล้ว";
            MainTabs.SelectedIndex = 2;
            await RefreshJobsAsync();
            await TryStartNextQueuedJobAsync();
        }
        catch (Exception exception)
        {
            ShowError("สร้าง Export Job ไม่สำเร็จ", exception);
        }
    }

    private async void BackendTimer_Tick(object? sender, EventArgs e)
    {
        if (_backendRefreshRunning || _project is null)
        {
            return;
        }

        _backendRefreshRunning = true;
        try
        {
            await RefreshJobsAsync();
            await TryStartNextQueuedJobAsync();
        }
        catch (Exception exception)
        {
            StatusBarText.Text = $"Job monitor warning: {exception.Message}";
        }
        finally
        {
            _backendRefreshRunning = false;
        }
    }

    private async Task RefreshJobsAsync()
    {
        if (_project is null)
        {
            JobsGrid.ItemsSource = null;
            return;
        }

        var jobs = (await _jobRepository.ListJobsAsync(_project)).ToList();
        var rows = new List<JobViewRow>();
        var selectedId = (JobsGrid.SelectedItem as JobViewRow)?.Job.JobId;

        foreach (var originalJob in jobs)
        {
            var job = originalJob;
            var jobDirectory = _jobRepository.GetJobDirectory(job);
            var progressPath = Path.Combine(jobDirectory, "progress.json");
            JobProgress? progress = null;

            if (File.Exists(progressPath))
            {
                try
                {
                    progress = await ReadJsonAsync<JobProgress>(progressPath);
                }
                catch
                {
                    // Worker may be replacing the file atomically; use the persisted job state this tick.
                }
            }

            var activeStatus = job.Status is JobStatuses.Preparing or JobStatuses.Processing or JobStatuses.Exporting;
            if (activeStatus &&
                job.WorkerProcessId is not null &&
                !WorkerLauncher.IsProcessAlive(job.WorkerProcessId) &&
                DateTimeOffset.UtcNow - job.UpdatedAt > TimeSpan.FromSeconds(3))
            {
                job = job with
                {
                    Status = JobStatuses.Resumable,
                    WorkerProcessId = null,
                    FailureMessage = "Worker ไม่ทำงานแล้ว สามารถ Resume โดยเริ่มขั้นตอน Export ใหม่"
                };
                await _jobRepository.WriteJobAsync(job);
            }

            await TailEventsAsync(job);

            rows.Add(new JobViewRow
            {
                Job = job,
                Id = job.JobId.ToString("N")[..10],
                Status = progress?.Status ?? job.Status,
                Progress = $"{progress?.Progress ?? 0:0.0}%",
                Agent = progress?.ActiveAgentId ?? "-",
                Message = progress?.Message ?? job.FailureMessage ?? "-",
                Output = job.OutputPath
            });

            if (job.Status is JobStatuses.Completed or JobStatuses.CompletedWithWarnings)
            {
                await RecordCompletedOutputAsync(job);
            }
        }

        JobsGrid.ItemsSource = rows;
        if (selectedId is not null)
        {
            JobsGrid.SelectedItem = rows.FirstOrDefault(row => row.Job.JobId == selectedId);
        }
    }

    private async Task TryStartNextQueuedJobAsync()
    {
        if (_project is null)
        {
            return;
        }

        var jobs = await _jobRepository.ListJobsAsync(_project);
        var active = jobs.Any(job =>
            (job.Status is JobStatuses.Preparing or JobStatuses.Processing or JobStatuses.Exporting or JobStatuses.Paused) &&
            (WorkerLauncher.IsProcessAlive(job.WorkerProcessId) ||
             (_recentlyLaunched.TryGetValue(job.JobId, out var launchedAt) &&
              DateTimeOffset.UtcNow - launchedAt < TimeSpan.FromSeconds(10))));

        if (active)
        {
            return;
        }

        var next = jobs
            .Where(job => job.Status == JobStatuses.Queued)
            .OrderBy(job => job.Priority)
            .ThenBy(job => job.CreatedAt)
            .FirstOrDefault();

        if (next is null)
        {
            return;
        }

        await WorkerLauncher.WriteControlAsync(next, "none");
        var processId = WorkerLauncher.Launch(next);
        _recentlyLaunched[next.JobId] = DateTimeOffset.UtcNow;
        StatusBarText.Text = $"เริ่ม Worker PID {processId} สำหรับ Job {next.JobId:N}";
    }

    private async Task TailEventsAsync(JobDocument job)
    {
        var path = Path.Combine(_jobRepository.GetJobDirectory(job), "agent-events.jsonl");
        if (!File.Exists(path))
        {
            return;
        }

        var offset = _eventOffsets.GetValueOrDefault(path);
        var length = new FileInfo(path).Length;
        if (offset > length)
        {
            offset = 0;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            32 * 1024,
            FileOptions.Asynchronous);
        stream.Seek(offset, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (await reader.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var agentEvent = JsonSerializer.Deserialize<AgentEvent>(line, JsonDefaults.Options);
                if (agentEvent is not null)
                {
                    _eventBus.Publish(agentEvent);
                }
            }
            catch (JsonException)
            {
                // Ignore a malformed final line and retry on the next tick.
            }
        }

        _eventOffsets[path] = stream.Position;
    }

    private async Task RecordCompletedOutputAsync(JobDocument job)
    {
        if (_project is null ||
            _project.OutputHistory.Any(item => item.JobId == job.JobId))
        {
            return;
        }

        var manifestPath = Path.Combine(_jobRepository.GetJobDirectory(job), "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return;
        }

        var manifest = await ReadJsonAsync<ExportManifest>(manifestPath);
        var history = _project.OutputHistory.ToList();
        history.Add(new OutputHistoryItem
        {
            JobId = job.JobId,
            OutputPath = manifest.OutputPath,
            Sha256 = manifest.Sha256,
            CreatedAt = manifest.CreatedAt
        });

        _project = _project with { OutputHistory = history };
        await _projectRepository.SaveAsync(_project);
    }

    private void EventBus_Published(object? sender, AgentEvent agentEvent)
    {
        if (agentEvent.AgentId is not null &&
            _agentUi.TryGetValue(agentEvent.AgentId, out var ui))
        {
            ui.Status.Text = agentEvent.Status;
            ui.Message.Text = agentEvent.Message;
            if (agentEvent.Progress is not null)
            {
                ui.Progress.Value = Math.Clamp(agentEvent.Progress.Value, 0, 100);
            }

            ui.Avatar.Background = StatusBrush(agentEvent.Status);
        }

        var line =
            $"{agentEvent.Timestamp.LocalDateTime:HH:mm:ss} | {agentEvent.EventType} | {agentEvent.AgentId ?? "-"} | {agentEvent.Status} | {agentEvent.Message}";
        AppendEventLog(line);
    }

    private static Brush StatusBrush(string status) => status switch
    {
        AgentStatuses.Rendering or AgentStatuses.Editing or AgentStatuses.Analysing or AgentStatuses.Reading
            => Brushes.DeepSkyBlue,
        AgentStatuses.Completed => Brushes.MediumSeaGreen,
        AgentStatuses.Warning or AgentStatuses.Paused => Brushes.Orange,
        AgentStatuses.Error => Brushes.IndianRed,
        AgentStatuses.Cancelled => Brushes.DarkGray,
        _ => Brushes.SlateGray
    };

    private void AppendEventLog(string line)
    {
        var lines = (AgentEventLogTextBox.Text + line + Environment.NewLine)
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .TakeLast(500);
        AgentEventLogTextBox.Text = string.Join(Environment.NewLine, lines) + Environment.NewLine;
        AgentEventLogTextBox.ScrollToEnd();
    }

    private async void PauseJob_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedJob() is not { } job)
        {
            return;
        }

        try
        {
            await WorkerLauncher.WriteControlAsync(job, "pause");
            StatusBarText.Text = "ส่งคำสั่ง Pause ไปยัง Worker แล้ว";
        }
        catch (Exception exception)
        {
            ShowError("Pause ไม่สำเร็จ", exception);
        }
    }

    private async void ResumeJob_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedJob() is not { } job)
        {
            return;
        }

        try
        {
            if (job.Status == JobStatuses.Paused && WorkerLauncher.IsProcessAlive(job.WorkerProcessId))
            {
                await WorkerLauncher.WriteControlAsync(job, "resume");
                StatusBarText.Text = "ส่งคำสั่ง Resume ไปยัง Worker แล้ว";
                return;
            }

            if (job.Status == JobStatuses.Resumable && !File.Exists(job.OutputPath))
            {
                job = job with
                {
                    Status = JobStatuses.Queued,
                    WorkerProcessId = null,
                    FailureMessage = null
                };
                await _jobRepository.WriteJobAsync(job);
                await WorkerLauncher.WriteControlAsync(job, "none");
                await TryStartNextQueuedJobAsync();
                StatusBarText.Text = "นำ Job กลับเข้าคิวแล้ว โดยจะเริ่มขั้นตอน Export ใหม่";
                return;
            }

            MessageBox.Show(
                "Job นี้ไม่อยู่ในสถานะที่ Resume ได้ หากล้มเหลวให้ใช้ Retry เป็น Version ใหม่",
                "AutoCut Studio");
        }
        catch (Exception exception)
        {
            ShowError("Resume ไม่สำเร็จ", exception);
        }
    }

    private async void CancelJob_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedJob() is not { } job)
        {
            return;
        }

        try
        {
            await WorkerLauncher.WriteControlAsync(job, "cancel");
            StatusBarText.Text = "ส่งคำสั่ง Cancel ไปยัง Worker แล้ว";
        }
        catch (Exception exception)
        {
            ShowError("Cancel ไม่สำเร็จ", exception);
        }
    }

    private async void RetryJob_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null || GetSelectedJob() is not { } original)
        {
            return;
        }

        var media = _project.SourceMedia.LastOrDefault(item =>
            string.Equals(item.SourcePath, original.InputPath, StringComparison.OrdinalIgnoreCase));

        if (media is null)
        {
            MessageBox.Show("ไม่พบ Source Media ใน Project กรุณา Relink Media ก่อน", "AutoCut Studio");
            return;
        }

        try
        {
            var retry = await _jobRepository.CreateTimelineExportJobAsync(
                _project,
                media,
                original.Segments);
            var history = _project.JobHistory.ToList();
            history.Add(retry.JobId);
            _project = _project with { JobHistory = history };
            await _projectRepository.SaveAsync(_project);
            await RefreshJobsAsync();
            await TryStartNextQueuedJobAsync();
            StatusBarText.Text = "สร้าง Retry Job พร้อม Output Version ใหม่แล้ว";
        }
        catch (Exception exception)
        {
            ShowError("Retry ไม่สำเร็จ", exception);
        }
    }

    private void OpenOutput_Click(object sender, RoutedEventArgs e)
    {
        var job = GetSelectedJob();
        if (job is null || !File.Exists(job.OutputPath))
        {
            MessageBox.Show("ยังไม่มี Output ที่เปิดได้", "AutoCut Studio");
            return;
        }

        Process.Start(new ProcessStartInfo(job.OutputPath)
        {
            UseShellExecute = true
        });
    }

    private void OpenJobFolder_Click(object sender, RoutedEventArgs e)
    {
        var job = GetSelectedJob();
        if (job is null)
        {
            return;
        }

        OpenFolder(_jobRepository.GetJobDirectory(job));
    }

    private void OpenProjectFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            return;
        }

        OpenFolder(_project.RootPath);
    }

    private static void OpenFolder(string path)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(path);
        Process.Start(startInfo);
    }

    private async void JobsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var job = GetSelectedJob();
        if (job is null)
        {
            JobLogTextBox.Text = string.Empty;
            return;
        }

        var path = Path.Combine(_jobRepository.GetJobDirectory(job), "run.log");
        try
        {
            JobLogTextBox.Text = File.Exists(path)
                ? await File.ReadAllTextAsync(path)
                : "ยังไม่มี Technical Log";
            JobLogTextBox.ScrollToEnd();
        }
        catch (IOException)
        {
            JobLogTextBox.Text = "Worker กำลังเขียน Log กรุณาลองเลือก Job อีกครั้ง";
        }
    }

    private JobDocument? GetSelectedJob()
        => (JobsGrid.SelectedItem as JobViewRow)?.Job;

    private async Task PublishProjectEventAsync(AgentEvent agentEvent)
    {
        _eventBus.Publish(agentEvent);
        if (_project is null)
        {
            return;
        }

        var path = Path.Combine(_project.RootPath, "logs", "agent-events.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var line = JsonSerializer.Serialize(agentEvent, JsonDefaults.CompactOptions);
        await File.AppendAllTextAsync(path, line + Environment.NewLine, new UTF8Encoding(false));
    }

    private async Task SaveRecentProjectPathAsync()
    {
        if (_project is null)
        {
            return;
        }

        var path = Path.Combine(AppPaths.SettingsDirectory, "last-project.txt");
        await File.WriteAllTextAsync(path, Path.Combine(_project.RootPath, "project.json"));
    }

    private static async Task<T> ReadJsonAsync<T>(string path)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            32 * 1024,
            FileOptions.Asynchronous);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonDefaults.Options)
               ?? throw new InvalidDataException($"อ่านข้อมูลจาก {path} ไม่สำเร็จ");
    }

    private string BuildSelectionSummary()
        => $"Playhead: {TimelineSlider.Value:0.000} | In: {_inPoint:0.000} | Out: {_outPoint:0.000} | Timeline: {_segments.Sum(item => item.DurationSeconds):0.000} s";

    private static List<TimelineSegment> CloneSegments(IEnumerable<TimelineSegment> segments)
        => segments.Select(CloneSegment).ToList();

    private static TimelineSegment CloneSegment(TimelineSegment segment) => new()
    {
        Id = segment.Id,
        StartSeconds = segment.StartSeconds,
        EndSeconds = segment.EndSeconds
    };

    private static void ShowError(string title, Exception exception)
    {
        MessageBox.Show(
            $"{exception.Message}\n\nSource Media ไม่ถูกแก้ไข\nสามารถตรวจ Technical Log ใน Job Center ได้เมื่อเป็นงานประมวลผล",
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private sealed record AgentUiRefs(
        Border Avatar,
        TextBlock Status,
        ProgressBar Progress,
        TextBlock Message);
}
