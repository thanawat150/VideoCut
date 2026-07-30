using System.Windows;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;
using AutoCutStudio.Infrastructure;
using Microsoft.Win32;

namespace AutoCutStudio.App;

public partial class AdvancedAiWindow : Window
{
    private readonly ProjectDocument _project;
    private readonly MediaAsset _media;
    private readonly TranscriptDocument? _transcript;
    private readonly ToolLocator _mediaTools;
    private readonly ComputerVisionToolLocator _visionTools = new();
    private readonly JobRepository _jobs;
    private readonly Func<AgentEvent, Task>? _eventSink;
    private ObjectAnalysisResult? _objects;
    private FaceTrackingResult? _faces;
    private List<DocumentSection> _sections = [];
    private string? _documentPath;

    public AdvancedAiWindow(
        ProjectDocument project,
        MediaAsset media,
        TranscriptDocument? transcript,
        ToolLocator mediaTools,
        JobRepository jobs,
        Func<AgentEvent, Task>? eventSink)
    {
        InitializeComponent();
        _project = project;
        _media = media;
        _transcript = transcript;
        _mediaTools = mediaTools;
        _jobs = jobs;
        _eventSink = eventSink;
        Loaded += AdvancedAiWindow_Loaded;
    }

    public List<JobDocument> CreatedJobs { get; } = [];

    private void AdvancedAiWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var availability = _visionTools.Locate();
        VisionStatusText.Text = $"{availability.Status}: {availability.Message}";
        VisionStatusText.Foreground = availability.IsReady
            ? System.Windows.Media.Brushes.LightGreen
            : System.Windows.Media.Brushes.Orange;
        AnalyzeObjectsButton.IsEnabled = availability.ObjectModelPath is not null;
        AnalyzeFacesButton.IsEnabled = availability.FaceModelPath is not null;
        if (_transcript is null)
            ObjectStatusText.Text = "ไม่มี Transcript: ตรวจ Object ได้ แต่ B-roll จากข้อความจะจำกัด";
    }

    private async void AnalyzeObjects_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(AnalyzeObjectsButton, true);
        ObjectStatusText.Text = "กำลังสุ่ม Frame และรัน YOLO Local...";
        try
        {
            await EmitAsync("object_detection.started", "detect_objects", AgentStatuses.Analysing, "เริ่ม YOLO Object Detection");
            _objects = await new OpenCvObjectDetector(_mediaTools, _visionTools).AnalyzeAsync(
                _media.SourcePath,
                _media.Metadata.DurationSeconds,
                sampleIntervalSeconds: 2);
            ObjectsGrid.ItemsSource = _objects.Detections
                .OrderBy(item => item.TimeSeconds)
                .ThenByDescending(item => item.Confidence)
                .Take(500)
                .ToList();
            var suggestions = _transcript is null
                ? Array.Empty<BrollSuggestion>()
                : new BrollSuggestionService().Suggest(
                    _transcript,
                    _objects,
                    Path.Combine(_project.RootPath, "assets"),
                    30).ToArray();
            BrollGrid.ItemsSource = suggestions;
            ObjectStatusText.Text =
                $"ตรวจพบ Object {_objects.Detections.Count:N0} รายการจาก Frame ตัวอย่าง | B-roll {suggestions.Length:N0} คำแนะนำ";
            await EmitAsync(
                "object_detection.completed",
                "detect_objects",
                AgentStatuses.Completed,
                ObjectStatusText.Text,
                new Dictionary<string, string>
                {
                    ["detections"] = _objects.Detections.Count.ToString(),
                    ["broll_suggestions"] = suggestions.Length.ToString()
                });
        }
        catch (Exception exception)
        {
            ObjectStatusText.Text = $"วิเคราะห์ Object ไม่สำเร็จ: {exception.Message}";
            await EmitAsync("object_detection.failed", "detect_objects", AgentStatuses.Error, exception.Message);
        }
        finally
        {
            SetBusy(AnalyzeObjectsButton, false);
        }
    }

    private async void AnalyzeFaces_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(AnalyzeFacesButton, true);
        FaceStatusText.Text = "กำลังสุ่ม Frame ตรวจใบหน้า และเชื่อมเป็น Track...";
        try
        {
            await EmitAsync("face_tracking.started", "track_faces", AgentStatuses.Analysing, "เริ่ม YuNet Face Tracking");
            _faces = await new OpenCvFaceTracker(_mediaTools, _visionTools).AnalyzeAsync(
                _media.SourcePath,
                _media.Metadata.Width,
                _media.Metadata.Height,
                _media.Metadata.DurationSeconds,
                sampleIntervalSeconds: 0.5);
            FaceTracksGrid.ItemsSource = _faces.Tracks;
            CreatePrivacyJobButton.IsEnabled = _faces.Tracks.Count > 0;
            FaceStatusText.Text = _faces.Tracks.Count == 0
                ? "ไม่พบ Face Track ที่ต่อเนื่องอย่างน้อย 2 Frame"
                : $"พบ Face Track {_faces.Tracks.Count:N0} รายการ — ตรวจและเลือก Track ที่ต้องการ Blur";
            await EmitAsync(
                "face_tracking.completed",
                "track_faces",
                AgentStatuses.Completed,
                FaceStatusText.Text,
                new Dictionary<string, string> { ["tracks"] = _faces.Tracks.Count.ToString() });
        }
        catch (Exception exception)
        {
            FaceStatusText.Text = $"Face Tracking ไม่สำเร็จ: {exception.Message}";
            await EmitAsync("face_tracking.failed", "track_faces", AgentStatuses.Error, exception.Message);
        }
        finally
        {
            SetBusy(AnalyzeFacesButton, false);
        }
    }

    private async void CreatePrivacyJob_Click(object sender, RoutedEventArgs e)
    {
        if (_faces is null) return;
        FaceTracksGrid.CommitEdit();
        var selected = _faces.Tracks.Where(track => track.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("เลือก Face Track ที่ต้องการ Blur", "AutoCut Studio");
            return;
        }
        try
        {
            var plan = new PrivacyBlurPlan
            {
                SelectedTracks = selected,
                BlurStrength = (int)BlurStrengthSlider.Value,
                BoxPaddingRatio = FacePaddingSlider.Value
            };
            var job = await new PrivacyBlurJobFactory(_jobs).CreateAsync(
                _project, _media, _faces, plan);
            CreatedJobs.Add(job);
            await EmitJobCreatedAsync(job, "privacy.job_created", "สร้าง Privacy Blur Job จาก Face Track ที่ตรวจสอบแล้ว");
            AdvancedStatusText.Text = $"สร้าง Privacy Blur Job {job.JobId:N} แล้ว";
            CreatePrivacyJobButton.IsEnabled = false;
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "สร้าง Privacy Job ไม่สำเร็จ");
        }
    }

    private async void BrowseDocument_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "เลือกเอกสารสำหรับสร้างวิดีโอ",
            Filter = "Supported documents|*.txt;*.md;*.markdown;*.docx;*.pdf"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            _documentPath = dialog.FileName;
            DocumentPathTextBox.Text = _documentPath;
            _sections = (await new DocumentContentReader().ReadAsync(_documentPath)).ToList();
            DocumentSectionsGrid.ItemsSource = _sections;
            CreateDocumentVideoButton.IsEnabled = _sections.Count > 0;
            AdvancedStatusText.Text = $"อ่านเอกสารแล้ว {_sections.Count:N0} Section สามารถแก้ก่อนสร้างได้";
        }
        catch (Exception exception)
        {
            AdvancedStatusText.Text = $"อ่านเอกสารไม่สำเร็จ: {exception.Message}";
        }
    }

    private async void CreateDocumentVideo_Click(object sender, RoutedEventArgs e)
    {
        if (_sections.Count == 0 || _documentPath is null) return;
        DocumentSectionsGrid.CommitEdit();
        var theme = (ThemeComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString()
                    ?? "documentary_dark";
        try
        {
            var recipe = new TemplateVideoRecipe
            {
                Title = Path.GetFileNameWithoutExtension(_documentPath),
                Width = 1920,
                Height = 1080,
                FrameRate = 30,
                ThemeId = theme,
                Sections = _sections,
                GenerateWindowsVoiceover = VoiceoverCheckBox.IsChecked == true
            };
            var job = await new TemplateVideoJobFactory(_jobs).CreateAsync(_project, recipe);
            CreatedJobs.Add(job);
            await EmitJobCreatedAsync(job, "template_video.job_created", "สร้าง Document-to-Video Job จาก Section ที่ตรวจสอบแล้ว");
            AdvancedStatusText.Text = $"สร้าง Document Video Job {job.JobId:N} แล้ว";
            CreateDocumentVideoButton.IsEnabled = false;
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "สร้าง Document Video Job ไม่สำเร็จ");
        }
    }

    private Task EmitAsync(
        string eventType,
        string action,
        string status,
        string message,
        Dictionary<string, string>? metadata = null) =>
        _eventSink?.Invoke(new AgentEvent
        {
            EventType = eventType,
            ProjectId = _project.ProjectId,
            AgentId = AgentIds.MediaAnalyst,
            Action = action,
            Status = status,
            Message = message,
            InputPath = _media.SourcePath,
            Metadata = metadata ?? []
        }) ?? Task.CompletedTask;

    private Task EmitJobCreatedAsync(JobDocument job, string eventType, string message) =>
        _eventSink?.Invoke(new AgentEvent
        {
            EventType = eventType,
            ProjectId = _project.ProjectId,
            JobId = job.JobId,
            AgentId = AgentIds.Producer,
            Action = job.JobType,
            Status = AgentStatuses.Completed,
            Progress = 100,
            Message = message,
            InputPath = job.InputPath,
            OutputPath = job.OutputPath
        }) ?? Task.CompletedTask;

    private static void SetBusy(System.Windows.Controls.Button button, bool busy)
    {
        button.IsEnabled = !busy;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = CreatedJobs.Count > 0;
    }
}
