using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Infrastructure;
using Microsoft.Win32;

namespace AutoCutStudio.App;

public partial class ProfessionalToolsWindow : Window
{
    private readonly ProjectDocument _project;
    private readonly MediaAsset? _activeMedia;
    private readonly IReadOnlyList<TimelineSegment> _timeline;
    private readonly JobRepository _jobs;
    private readonly ToolLocator _tools;
    private readonly Func<JobDocument, Task> _onJobCreated;
    private readonly ProfessionalJobFactory _jobFactory;
    private readonly NestedSequenceRepository _nestedRepository = new();
    private readonly DeliveryPackageService _delivery = new();
    private readonly CollaborationPackageService _collaboration = new();
    private readonly ObservableCollection<AngleView> _angles = [];
    private readonly ObservableCollection<NestedView> _nested = [];
    private string? _deliveryVideo;
    private string? _deliveryFolder;

    public ProfessionalToolsWindow(
        ProjectDocument project,
        MediaAsset? activeMedia,
        IReadOnlyList<TimelineSegment> timeline,
        JobRepository jobs,
        ToolLocator tools,
        Func<JobDocument, Task> onJobCreated)
    {
        InitializeComponent();
        _project = project;
        _activeMedia = activeMedia;
        _timeline = timeline;
        _jobs = jobs;
        _tools = tools;
        _onJobCreated = onJobCreated;
        _jobFactory = new ProfessionalJobFactory(jobs);
        AnglesListBox.ItemsSource = _angles;
        NestedListBox.ItemsSource = _nested;
        ProvidersGrid.ItemsSource = _delivery.GetCapabilities();
        if (_activeMedia is not null) AddAngle(_activeMedia.SourcePath, _activeMedia.Metadata.HasAudio);
        Loaded += async (_, _) =>
        {
            await RefreshNestedAsync();
            await RefreshPluginsAsync();
            ApplyDefaultTimes();
        };
    }

    private void ApplyDefaultTimes()
    {
        if (_activeMedia is null) return;
        var duration = Math.Min(6, _activeMedia.Metadata.DurationSeconds);
        var middle = Math.Max(0.5, duration / 2);
        KeyframePlanTextBox.Text = string.Create(CultureInfo.InvariantCulture,
            $"0,1,0.5,0.5,1{Environment.NewLine}{middle:0.###},1.25,0.4,0.5,1{Environment.NewLine}{duration:0.###},1,0.5,0.5,1");
        SwitchPlanTextBox.Text = string.Create(CultureInfo.InvariantCulture,
            $"1,0,{middle:0.###}{Environment.NewLine}2,{middle:0.###},{duration:0.###}");
    }

    private async void AddAngles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "เลือกไฟล์กล้องสำหรับ Multicam",
            Filter = "MP4 Video|*.mp4",
            Multiselect = true
        };
        if (dialog.ShowDialog() != true) return;
        SetBusy("กำลังตรวจไฟล์กล้องด้วย FFprobe...");
        try
        {
            var probe = new FfprobeMediaProbe(_tools);
            foreach (var path in dialog.FileNames)
            {
                if (_angles.Any(item => item.SourcePath.Equals(path, StringComparison.OrdinalIgnoreCase))) continue;
                var metadata = await probe.ProbeAsync(path);
                AddAngle(path, metadata.HasAudio);
            }
            ProfessionalStatusText.Text = $"มีกล้อง {_angles.Count} มุม";
        }
        catch (Exception exception) { ShowError("เพิ่มกล้องไม่สำเร็จ", exception); }
    }

    private void ClearAngles_Click(object sender, RoutedEventArgs e)
    {
        var first = _angles.FirstOrDefault();
        _angles.Clear();
        if (first is not null) _angles.Add(first);
    }

    private void AddAngle(string path, bool hasAudio)
    {
        _angles.Add(new AngleView
        {
            Number = _angles.Count + 1,
            AngleId = Guid.NewGuid(),
            SourcePath = Path.GetFullPath(path),
            DisplayName = Path.GetFileName(path),
            HasAudio = hasAudio
        });
    }

    private async void CreateMulticam_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_angles.Count < 2) throw new InvalidDataException("เพิ่มกล้องอย่างน้อย 2 ไฟล์");
            if (_angles.Any(item => !item.HasAudio))
                throw new InvalidDataException("Multicam รุ่นนี้ต้องการ Audio Stream ในทุกกล้องเพื่อให้ concat ได้อย่างปลอดภัย");
            var switches = ParseSwitches();
            var recipe = new MulticamRenderRecipe
            {
                Angles = _angles.Select(item => new CameraAngle
                {
                    AngleId = item.AngleId,
                    DisplayName = item.DisplayName,
                    SourcePath = item.SourcePath,
                    HasAudio = item.HasAudio,
                    OffsetSeconds = item.OffsetSeconds
                }).ToList(),
                Switches = switches,
                OutputWidth = 1920,
                OutputHeight = 1080,
                FrameRate = 30
            };
            var job = await _jobFactory.CreateMulticamAsync(_project, recipe);
            await RegisterAsync(job, "สร้าง Multicam Job แล้ว");
        }
        catch (Exception exception) { ShowError("สร้าง Multicam Job ไม่สำเร็จ", exception); }
    }

    private List<MulticamSwitch> ParseSwitches()
    {
        var result = new List<MulticamSwitch>();
        foreach (var line in Lines(SwitchPlanTextBox.Text))
        {
            var values = line.Split(',', StringSplitOptions.TrimEntries);
            if (values.Length != 3 || !int.TryParse(values[0], out var number) ||
                !TryDouble(values[1], out var start) || !TryDouble(values[2], out var end))
                throw new InvalidDataException($"อ่าน Switch Plan ไม่ได้: {line}");
            var angle = _angles.FirstOrDefault(item => item.Number == number)
                        ?? throw new InvalidDataException($"ไม่พบกล้องหมายเลข {number}");
            if (start < 0 || end <= start) throw new InvalidDataException($"ช่วงเวลาไม่ถูกต้อง: {line}");
            result.Add(new MulticamSwitch
            {
                Sequence = result.Count + 1,
                AngleId = angle.AngleId,
                SourceStartSeconds = start,
                SourceEndSeconds = end
            });
        }
        if (result.Count == 0) throw new InvalidDataException("Switch Plan ว่าง");
        return result;
    }

    private async void CreateKeyframe_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_activeMedia is null) throw new InvalidDataException("เปิดวิดีโอหลักก่อนสร้าง Keyframe Job");
            var frames = new List<MotionKeyframe>();
            foreach (var line in Lines(KeyframePlanTextBox.Text))
            {
                var values = line.Split(',', StringSplitOptions.TrimEntries);
                if (values.Length != 5 || !TryDouble(values[0], out var time) || !TryDouble(values[1], out var zoom) ||
                    !TryDouble(values[2], out var x) || !TryDouble(values[3], out var y) || !TryDouble(values[4], out var gain))
                    throw new InvalidDataException($"อ่าน Keyframe ไม่ได้: {line}");
                frames.Add(new MotionKeyframe
                {
                    Sequence = frames.Count + 1,
                    TimeSeconds = time,
                    Zoom = zoom,
                    FocusX = x,
                    FocusY = y,
                    AudioGain = gain
                });
            }
            var job = await _jobFactory.CreateKeyframeAsync(_project, _activeMedia, new KeyframeRenderRecipe
            {
                Keyframes = frames,
                OutputWidth = _activeMedia.Metadata.Width > 0 ? _activeMedia.Metadata.Width : 1920,
                OutputHeight = _activeMedia.Metadata.Height > 0 ? _activeMedia.Metadata.Height : 1080,
                FrameRate = 30
            });
            await RegisterAsync(job, "สร้าง Keyframe Job แล้ว");
        }
        catch (Exception exception) { ShowError("สร้าง Keyframe Job ไม่สำเร็จ", exception); }
    }

    private async void SaveNested_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_activeMedia is null || _timeline.Count == 0)
                throw new InvalidDataException("Timeline ปัจจุบันไม่มี Clip สำหรับบันทึก");
            var sequence = new NestedSequenceDocument
            {
                ProjectId = _project.ProjectId,
                Name = string.IsNullOrWhiteSpace(NestedNameTextBox.Text) ? "Nested Sequence" : NestedNameTextBox.Text.Trim(),
                Clips = _timeline.OrderBy(item => item.StartSeconds).Select((item, index) => new NestedSequenceClip
                {
                    Sequence = index + 1,
                    SourcePath = _activeMedia.SourcePath,
                    SourceStartSeconds = item.StartSeconds,
                    SourceEndSeconds = item.EndSeconds,
                    HasAudio = _activeMedia.Metadata.HasAudio
                }).ToList()
            };
            await _nestedRepository.SaveAsync(_project, sequence);
            await RefreshNestedAsync();
            ProfessionalStatusText.Text = "บันทึก Nested Sequence แล้ว";
        }
        catch (Exception exception) { ShowError("บันทึก Nested Sequence ไม่สำเร็จ", exception); }
    }

    private async void RefreshNested_Click(object sender, RoutedEventArgs e) => await RefreshNestedAsync();

    private async Task RefreshNestedAsync()
    {
        var documents = await _nestedRepository.ListAsync(_project);
        _nested.Clear();
        foreach (var document in documents) _nested.Add(new NestedView(document));
    }

    private async void RenderNested_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (NestedListBox.SelectedItem is not NestedView selected)
                throw new InvalidDataException("เลือก Nested Sequence ก่อน");
            var recipe = new NestedSequenceRenderRecipe
            {
                SequenceId = selected.Document.SequenceId,
                Name = selected.Document.Name,
                Clips = selected.Document.Clips,
                OutputWidth = 1920,
                OutputHeight = 1080,
                FrameRate = 30
            };
            var job = await _jobFactory.CreateNestedSequenceAsync(_project, recipe);
            await RegisterAsync(job, "สร้าง Nested Sequence Job แล้ว");
        }
        catch (Exception exception) { ShowError("Render Nested Sequence ไม่สำเร็จ", exception); }
    }

    private async void RefreshPlugins_Click(object sender, RoutedEventArgs e) => await RefreshPluginsAsync();

    private async Task RefreshPluginsAsync()
    {
        PluginsGrid.ItemsSource = await new PluginCatalogService().DiscoverAsync(_project);
    }

    private void BrowseDeliveryVideo_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "MP4 Video|*.mp4", Title = "เลือก Output ที่ผ่าน QA" };
        if (dialog.ShowDialog() != true) return;
        _deliveryVideo = dialog.FileName;
        DeliveryVideoTextBox.Text = _deliveryVideo;
    }

    private void BrowseDeliveryFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "เลือก Local / Cloud Sync Folder" };
        if (dialog.ShowDialog() != true) return;
        _deliveryFolder = dialog.FolderName;
        DeliveryFolderTextBox.Text = _deliveryFolder;
    }

    private async void DeliverFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_deliveryVideo is null || _deliveryFolder is null) throw new InvalidDataException("เลือกวิดีโอและโฟลเดอร์ก่อน");
            var manifest = await _delivery.DeliverToFolderAsync(_deliveryVideo, _deliveryFolder);
            ProfessionalStatusText.Text = $"ส่งมอบไฟล์แล้ว: {manifest.DeliveredPath}";
        }
        catch (Exception exception) { ShowError("ส่งมอบไฟล์ไม่สำเร็จ", exception); }
    }

    private async void CreateSocialOutbox_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_deliveryVideo is null || _deliveryFolder is null) throw new InvalidDataException("เลือกวิดีโอและโฟลเดอร์ Outbox ก่อน");
            var platform = (PlatformComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "youtube";
            var package = await _delivery.CreateSocialOutboxAsync(_deliveryVideo, _deliveryFolder, new SocialPublishMetadata
            {
                Platform = platform,
                Title = PublishTitleTextBox.Text.Trim(),
                Privacy = "private"
            });
            ProfessionalStatusText.Text = $"สร้าง Social Publishing Package แล้ว: {package}";
        }
        catch (Exception exception) { ShowError("สร้าง Social Publishing Package ไม่สำเร็จ", exception); }
    }

    private async void ExportCollaboration_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export Collaboration Package",
            Filter = "AutoCut Collaboration ZIP|*.zip",
            FileName = _project.DisplayName + "_collaboration.zip"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            SetBusy("กำลังสร้าง Collaboration Package...");
            var manifest = await _collaboration.ExportAsync(_project, dialog.FileName, IncludeMediaCheckBox.IsChecked == true);
            CollaborationLogTextBox.Text =
                $"Package: {dialog.FileName}{Environment.NewLine}Files: {manifest.Entries.Count}{Environment.NewLine}Includes media: {manifest.IncludesSourceMedia}";
            ProfessionalStatusText.Text = "Export Collaboration Package สำเร็จ";
        }
        catch (Exception exception) { ShowError("Export Collaboration Package ไม่สำเร็จ", exception); }
    }

    private async void ImportCollaboration_Click(object sender, RoutedEventArgs e)
    {
        var packageDialog = new OpenFileDialog { Filter = "AutoCut Collaboration ZIP|*.zip" };
        if (packageDialog.ShowDialog() != true) return;
        var folderDialog = new OpenFolderDialog { Title = "เลือกโฟลเดอร์ว่างสำหรับ Import" };
        if (folderDialog.ShowDialog() != true) return;
        var destination = Path.Combine(folderDialog.FolderName, Path.GetFileNameWithoutExtension(packageDialog.FileName));
        try
        {
            SetBusy("กำลังตรวจ SHA-256 และ Import Package...");
            var manifest = await _collaboration.ImportAsync(packageDialog.FileName, destination);
            CollaborationLogTextBox.Text =
                $"Imported to: {destination}{Environment.NewLine}Project: {manifest.ProjectName}{Environment.NewLine}Verified files: {manifest.Entries.Count}";
            ProfessionalStatusText.Text = "Import และตรวจ Checksum สำเร็จ";
        }
        catch (Exception exception) { ShowError("Import Collaboration Package ไม่สำเร็จ", exception); }
    }

    private async Task RegisterAsync(JobDocument job, string message)
    {
        await _onJobCreated(job);
        ProfessionalStatusText.Text = message;
    }

    private void SetBusy(string message) => ProfessionalStatusText.Text = message;
    private static IEnumerable<string> Lines(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    private static bool TryDouble(string value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ||
        double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result);

    private void ShowError(string title, Exception exception)
    {
        ProfessionalStatusText.Text = exception.Message;
        MessageBox.Show(exception.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private sealed class AngleView
    {
        public int Number { get; init; }
        public Guid AngleId { get; init; }
        public string DisplayName { get; init; } = string.Empty;
        public string SourcePath { get; init; } = string.Empty;
        public bool HasAudio { get; init; }
        public double OffsetSeconds { get; set; }
        public string DisplaySummary => $"{Number}. {DisplayName} | Audio: {(HasAudio ? "Yes" : "No")} | Offset {OffsetSeconds:0.###}s | {SourcePath}";
    }

    private sealed class NestedView
    {
        public NestedView(NestedSequenceDocument document) => Document = document;
        public NestedSequenceDocument Document { get; }
        public string DisplaySummary => $"{Document.Name} | {Document.Clips.Count} clips | {Document.Clips.Sum(item => item.DurationSeconds):0.0}s";
    }
}
