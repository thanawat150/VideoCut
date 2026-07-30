using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;

namespace AutoCutStudio;

public partial class MainWindow : Window
{
    private readonly FfprobeService _probe = new();
    private readonly PersistentJobQueue _jobQueue = new();
    private readonly DispatcherTimer _positionTimer;
    private readonly ObservableCollection<TimelineClip> _timeline = [];
    private ProjectDocument? _project;
    private string? _projectDirectory;

    public MainWindow()
    {
        InitializeComponent();
        TimelineList.ItemsSource = _timeline;
        MediaList.DisplayMemberPath = nameof(MediaItem.FileName);
        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _positionTimer.Tick += (_, _) => RefreshPositionText();
        _positionTimer.Start();
    }

    private ProjectService CreateProjectService() => new(_probe);

    private async void NewProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "เลือกตำแหน่งและตั้งชื่อโปรเจกต์",
            Filter = "AutoCut Studio Project (*.autocut)|*.autocut",
            FileName = "My Video.autocut",
            AddExtension = true,
            OverwritePrompt = false
        };
        if (dialog.ShowDialog(this) != true) return;

        await RunUiActionAsync("กำลังสร้างโปรเจกต์...", async () =>
        {
            var parent = Path.GetDirectoryName(dialog.FileName)!;
            var name = Path.GetFileNameWithoutExtension(dialog.FileName);
            var result = await CreateProjectService().CreateAsync(parent, name);
            _project = result.Project;
            _projectDirectory = result.ProjectDirectory;
            BindProject();
            StatusText.Text = $"สร้างโปรเจกต์แล้ว: {_project.Name}";
        });
    }

    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "เปิด project.json",
            Filter = "AutoCut Studio Project (project.json)|project.json|JSON (*.json)|*.json"
        };
        if (dialog.ShowDialog(this) != true) return;

        await RunUiActionAsync("กำลังเปิดโปรเจกต์...", async () =>
        {
            _project = await CreateProjectService().LoadAsync(dialog.FileName);
            _projectDirectory = Path.GetDirectoryName(dialog.FileName);
            BindProject();
            StatusText.Text = $"เปิดโปรเจกต์แล้ว: {_project.Name}";
        });
    }

    private async void ImportMedia_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProject()) return;
        var dialog = new OpenFileDialog
        {
            Title = "นำเข้าวิดีโอ",
            Filter = "Video files|*.mp4;*.mov;*.mkv;*.avi;*.webm;*.mts;*.m2ts;*.mxf;*.mpeg;*.3gp|All files|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;

        await RunUiActionAsync("กำลังตรวจและนำเข้าสื่อด้วย FFprobe...", async () =>
        {
            foreach (var file in dialog.FileNames)
            {
                StatusText.Text = $"กำลังนำเข้า {Path.GetFileName(file)}";
                await CreateProjectService().ImportMediaAsync(_project!, _projectDirectory!, file);
            }
            BindProject();
            StatusText.Text = $"นำเข้าแล้ว {dialog.FileNames.Length} ไฟล์";
        });
    }

    private async void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProject()) return;
        await RunUiActionAsync("กำลังบันทึก...", async () =>
        {
            await CreateProjectService().SaveAsync(_project!, _projectDirectory!);
            StatusText.Text = "บันทึกโปรเจกต์แล้ว";
        });
    }

    private void MediaList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MediaList.SelectedItem is not MediaItem media || _projectDirectory is null) return;
        LoadPreview(media, TimeSpan.Zero);
    }

    private void PreviewPlayer_MediaOpened(object sender, RoutedEventArgs e) => RefreshPositionText();
    private void Play_Click(object sender, RoutedEventArgs e) => PreviewPlayer.Play();
    private void Pause_Click(object sender, RoutedEventArgs e) => PreviewPlayer.Pause();
    private void Stop_Click(object sender, RoutedEventArgs e) => PreviewPlayer.Stop();

    private async void Split_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedClip(out var clip)) return;
        var splitAt = PreviewPlayer.Position;
        if (splitAt <= clip.SourceIn || splitAt >= clip.SourceOut)
        {
            ShowWarning("ตำแหน่งแยกต้องอยู่ภายในช่วงของคลิปที่เลือก");
            return;
        }

        var index = _timeline.IndexOf(clip);
        var left = new TimelineClip
        {
            MediaId = clip.MediaId,
            DisplayName = clip.DisplayName + " A",
            SourceIn = clip.SourceIn,
            SourceOut = splitAt,
            TimelineStart = clip.TimelineStart
        };
        var right = new TimelineClip
        {
            MediaId = clip.MediaId,
            DisplayName = clip.DisplayName + " B",
            SourceIn = splitAt,
            SourceOut = clip.SourceOut,
            TimelineStart = clip.TimelineStart + left.Duration
        };
        _timeline.RemoveAt(index);
        _timeline.Insert(index, left);
        _timeline.Insert(index + 1, right);
        SyncTimelineToProject();
        await SaveCurrentProjectAsync("แยกคลิปแล้ว");
    }

    private async void SetIn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedClip(out var clip)) return;
        if (PreviewPlayer.Position >= clip.SourceOut)
        {
            ShowWarning("จุดเริ่มต้องอยู่ก่อนจุดจบ");
            return;
        }
        clip.SourceIn = PreviewPlayer.Position;
        TimelineList.Items.Refresh();
        SyncTimelineToProject();
        await SaveCurrentProjectAsync("ปรับจุดเริ่มแล้ว");
    }

    private async void SetOut_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedClip(out var clip)) return;
        if (PreviewPlayer.Position <= clip.SourceIn)
        {
            ShowWarning("จุดจบต้องอยู่หลังจุดเริ่ม");
            return;
        }
        clip.SourceOut = PreviewPlayer.Position;
        TimelineList.Items.Refresh();
        SyncTimelineToProject();
        await SaveCurrentProjectAsync("ปรับจุดจบแล้ว");
    }

    private async void DeleteClip_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedClip(out var clip)) return;
        _timeline.Remove(clip);
        SyncTimelineToProject();
        await SaveCurrentProjectAsync("ลบคลิปจาก Timeline แล้ว ไฟล์ต้นฉบับยังอยู่ครบ");
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedClip(out var clip) || _project is null || _projectDirectory is null) return;
        var media = _project.SourceFiles.FirstOrDefault(item => item.Id == clip.MediaId);
        if (media is null)
        {
            ShowWarning("ไม่พบ Media ที่เชื่อมกับคลิปนี้");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export MP4",
            Filter = "MP4 Video (*.mp4)|*.mp4",
            InitialDirectory = Path.Combine(_projectDirectory, "exports"),
            FileName = Path.GetFileNameWithoutExtension(media.FileName) + "-export.mp4",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;
        if (File.Exists(dialog.FileName)) File.Delete(dialog.FileName);

        var request = new ExportRequest
        {
            InputPath = Path.Combine(_projectDirectory, media.ProjectPath),
            OutputPath = dialog.FileName,
            Start = clip.SourceIn,
            Duration = clip.Duration,
            Settings = _project.ExportSettings
        };

        await RunUiActionAsync("กำลังเตรียม Export...", async () =>
        {
            JobProgress.Value = 0;
            var job = await _jobQueue.EnqueueAsync(request);
            await RunWorkerProcessAsync(Environment.ProcessPath
                ?? throw new InvalidOperationException("ไม่พบตำแหน่ง AutoCutStudio.exe"), job);

            _project.ProcessingHistory.Add(new ProcessingHistoryItem { Action = "export", Result = dialog.FileName });
            await CreateProjectService().SaveAsync(_project, _projectDirectory);
            StatusText.Text = $"Export สำเร็จ: {dialog.FileName}";
            MessageBox.Show(this, "สร้างวิดีโอสำเร็จและไม่ได้แก้ไขไฟล์ต้นฉบับ", "AutoCut Studio", MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }

    private async Task RunWorkerProcessAsync(string workerPath, JobRecord job)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = workerPath,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--worker");
        startInfo.ArgumentList.Add("--once");
        using var worker = Process.Start(startInfo) ?? throw new InvalidOperationException("เปิด Worker ไม่สำเร็จ");
        var progressFile = Path.Combine(_jobQueue.JobsRoot, job.JobId.ToString("N"), "progress.json");
        while (!worker.HasExited)
        {
            await Task.Delay(250);
            try
            {
                if (!File.Exists(progressFile)) continue;
                await using var stream = new FileStream(progressFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var json = await JsonDocument.ParseAsync(stream);
                if (json.RootElement.TryGetProperty("percent", out var value)) JobProgress.Value = value.GetInt32();
            }
            catch (IOException) { }
            catch (JsonException) { }
        }
        await worker.WaitForExitAsync();
        if (!File.Exists(job.Export!.OutputPath))
            throw new InvalidOperationException("Worker จบการทำงานแต่ไม่พบไฟล์ผลลัพธ์ โปรดเปิด error.log ในโฟลเดอร์ Job");
        JobProgress.Value = 100;
    }

    private void BindProject()
    {
        if (_project is null) return;
        ProjectTitle.Text = _project.Name;
        MediaList.ItemsSource = null;
        MediaList.ItemsSource = _project.SourceFiles;
        _timeline.Clear();
        foreach (var clip in _project.Tracks[0].Clips) _timeline.Add(clip);
    }

    private void SyncTimelineToProject()
    {
        if (_project is null) return;
        var track = _project.Tracks[0];
        track.Clips.Clear();
        var cursor = TimeSpan.Zero;
        foreach (var clip in _timeline)
        {
            clip.TimelineStart = cursor;
            cursor += clip.Duration;
            track.Clips.Add(clip);
        }
    }

    private bool TryGetSelectedClip(out TimelineClip clip)
    {
        clip = TimelineList.SelectedItem as TimelineClip ?? null!;
        if (clip is null)
        {
            ShowWarning("กรุณาเลือกคลิปใน Timeline ก่อน");
            return false;
        }

        if (_project is not null && _projectDirectory is not null)
        {
            var selectedClip = clip;
            var media = _project.SourceFiles.FirstOrDefault(item => item.Id == selectedClip.MediaId);
            if (media is not null && PreviewPlayer.Source?.LocalPath != Path.Combine(_projectDirectory, media.ProjectPath))
                LoadPreview(media, selectedClip.SourceIn);
        }
        return true;
    }

    private void LoadPreview(MediaItem media, TimeSpan position)
    {
        if (_projectDirectory is null) return;
        var path = Path.GetFullPath(Path.Combine(_projectDirectory, media.ProjectPath));
        if (!File.Exists(path))
        {
            ShowWarning($"ไม่พบไฟล์: {path}\nต้องใช้ Relink Missing Media ซึ่งยังไม่รองรับใน Phase 1");
            return;
        }
        PreviewPlayer.Stop();
        PreviewPlayer.Source = new Uri(path);
        PreviewPlayer.Position = position;
    }

    private async Task SaveCurrentProjectAsync(string status)
    {
        if (_project is null || _projectDirectory is null) return;
        await CreateProjectService().SaveAsync(_project, _projectDirectory);
        StatusText.Text = status;
    }

    private bool EnsureProject()
    {
        if (_project is not null && _projectDirectory is not null) return true;
        ShowWarning("กรุณาสร้างหรือเปิดโปรเจกต์ก่อน");
        return false;
    }

    private async Task RunUiActionAsync(string status, Func<Task> action)
    {
        try
        {
            IsEnabled = false;
            StatusText.Text = status;
            await action();
        }
        catch (Exception ex)
        {
            StatusText.Text = "งานไม่สำเร็จ — ไฟล์ต้นฉบับไม่ได้รับผลกระทบ";
            MessageBox.Show(this,
                $"เกิดอะไรขึ้น: {ex.Message}\n\nขั้นตอน: {status}\nไฟล์ต้นฉบับ: ไม่ถูกแก้ไข\n\nรายละเอียดทางเทคนิค:\n{ex}",
                "AutoCut Studio — Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void RefreshPositionText()
    {
        var total = PreviewPlayer.NaturalDuration.HasTimeSpan ? PreviewPlayer.NaturalDuration.TimeSpan : TimeSpan.Zero;
        PositionText.Text = $"{PreviewPlayer.Position:hh\\:mm\\:ss} / {total:hh\\:mm\\:ss}";
    }

    private void ShowWarning(string message) =>
        MessageBox.Show(this, message, "AutoCut Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
}
