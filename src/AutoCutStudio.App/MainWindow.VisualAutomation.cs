using System.Windows;
using System.Windows.Controls;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Infrastructure;
using Microsoft.Win32;

namespace AutoCutStudio.App;

public partial class MainWindow
{
    private bool _visualAutomationUiEnabled;
    private AutomationStudioWindow? _automationStudioWindow;
    private MobileControlServer? _mobileControlServer;

    public void EnableVisualAutomationUi()
    {
        if (_visualAutomationUiEnabled)
        {
            return;
        }
        _visualAutomationUiEnabled = true;

        if (CommandTextBox.Parent is DockPanel commandPanel)
        {
            var automationButton = new Button
            {
                Content = "◆ Automation Studio",
                ToolTip = "เปิด Visual Video Automation แบบ Make / n8n"
            };
            automationButton.SetResourceReference(StyleProperty, "PrimaryButtonStyle");
            automationButton.Click += OpenAutomationStudio_Click;
            commandPanel.Children.Add(automationButton);

            var multiImportButton = new Button
            {
                Content = "＋ Import หลายคลิป",
                ToolTip = "เลือกหลาย MP4 พร้อมกัน"
            };
            multiImportButton.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
            multiImportButton.Click += ImportMultipleMedia_Click;
            commandPanel.Children.Add(multiImportButton);

            var folderButton = new Button
            {
                Content = "▣ Import โฟลเดอร์",
                ToolTip = "นำเข้า MP4 ทุกไฟล์ในโฟลเดอร์"
            };
            folderButton.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
            folderButton.Click += ImportMediaFolder_Click;
            commandPanel.Children.Add(folderButton);

            var mobileButton = new Button
            {
                Content = "◉ Mobile Control",
                ToolTip = "ควบคุมผ่านมือถือใน Wi-Fi เดียวกัน"
            };
            mobileButton.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
            mobileButton.Click += MobileControl_Click;
            commandPanel.Children.Add(mobileButton);
        }

        CommandTextBox.Text = "เลือก Automation Template หรือพิมพ์คำสั่ง เช่น ตัดเงียบ ใส่ซับ และสร้าง Shorts";
        StatusBarText.Text = "Visual Video Automation พร้อม — Flow Builder, Multi-clip, Platform Safe Zone และ Mobile Control";
    }

    private void OpenAutomationStudio_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            MessageBox.Show("กรุณาสร้างหรือเปิด Project ก่อน", "Automation Studio");
            return;
        }

        if (_automationStudioWindow is { IsVisible: true })
        {
            _automationStudioWindow.Activate();
            return;
        }

        _automationStudioWindow = new AutomationStudioWindow(
            _project,
            async (updatedProject, jobs) =>
            {
                _project = updatedProject;
                await RefreshJobsAsync();
                await TryStartNextQueuedJobAsync();
                StatusBarText.Text = jobs.Count == 0
                    ? "Automation ทำงานเสร็จ โดยยังไม่มี Export Job"
                    : $"Automation สร้าง {jobs.Count} Job และเริ่มคิวประมวลผลแล้ว";
            },
            () =>
            {
                MainTabs.SelectedIndex = 2;
                Activate();
            })
        {
            Owner = this
        };
        _automationStudioWindow.Closed += (_, _) => _automationStudioWindow = null;
        _automationStudioWindow.Show();
    }

    private async void ImportMultipleMedia_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            MessageBox.Show("กรุณาสร้างหรือเปิด Project ก่อน", "AutoCut Studio");
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "เลือกวิดีโอหลายคลิป",
            Filter = "MP4 video|*.mp4",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog() == true)
        {
            await ImportMediaPathsAsync(dialog.FileNames);
        }
    }

    private async void ImportMediaFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            MessageBox.Show("กรุณาสร้างหรือเปิด Project ก่อน", "AutoCut Studio");
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "เลือกโฟลเดอร์ที่มีคลิป MP4",
            Multiselect = false
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var paths = Directory.EnumerateFiles(
                dialog.FolderName,
                "*.mp4",
                SearchOption.TopDirectoryOnly)
            .OrderBy(File.GetCreationTimeUtc)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length == 0)
        {
            MessageBox.Show("ไม่พบไฟล์ MP4 ในโฟลเดอร์นี้", "AutoCut Studio");
            return;
        }
        await ImportMediaPathsAsync(paths);
    }

    private async Task ImportMediaPathsAsync(IReadOnlyList<string> paths)
    {
        if (_project is null || paths.Count == 0)
        {
            return;
        }

        try
        {
            RefreshToolStatus();
            if (!_toolAvailability.IsReady)
            {
                MessageBox.Show(_toolAvailability.Message, "FFmpeg Dependency ไม่พร้อม");
                return;
            }

            var sourceMedia = _project.SourceMedia.ToList();
            var existing = sourceMedia
                .Select(item => Path.GetFullPath(item.SourcePath))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var imported = new List<MediaAsset>();
            var failed = new List<string>();
            var probe = new FfprobeMediaProbe(_toolLocator);

            for (var index = 0; index < paths.Count; index++)
            {
                var path = Path.GetFullPath(paths[index]);
                StatusBarText.Text = $"กำลังตรวจคลิป {index + 1}/{paths.Count}: {Path.GetFileName(path)}";
                if (!File.Exists(path) || existing.Contains(path))
                {
                    continue;
                }

                try
                {
                    var metadata = await probe.ProbeAsync(path);
                    if (!metadata.HasVideo || metadata.DurationSeconds <= 0)
                    {
                        failed.Add($"{Path.GetFileName(path)}: ไม่มี Video Stream");
                        continue;
                    }

                    var asset = new MediaAsset
                    {
                        SourcePath = path,
                        Metadata = metadata,
                        ImportedAt = DateTimeOffset.UtcNow
                    };
                    sourceMedia.Add(asset);
                    imported.Add(asset);
                    existing.Add(path);
                }
                catch (Exception exception)
                {
                    failed.Add($"{Path.GetFileName(path)}: {exception.Message}");
                }
            }

            if (imported.Count == 0)
            {
                MessageBox.Show(
                    failed.Count == 0
                        ? "คลิปที่เลือกถูกนำเข้าไว้แล้ว"
                        : string.Join(Environment.NewLine, failed.Take(12)),
                    "Import หลายคลิป");
                return;
            }

            _activeMedia = imported[^1];
            _segments =
            [
                new TimelineSegment
                {
                    StartSeconds = 0,
                    EndSeconds = _activeMedia.Metadata.DurationSeconds
                }
            ];
            _project = _project with
            {
                SourceMedia = sourceMedia,
                Timeline = new TimelineDocument
                {
                    MediaAssetId = _activeMedia.Id,
                    Segments = CloneSegments(_segments)
                }
            };
            await _projectRepository.SaveAsync(_project);
            await SaveRecentProjectPathAsync();
            LoadProjectIntoUi();
            LoadActiveMediaIntoPlayer();
            StatusBarText.Text = $"Import สำเร็จ {imported.Count} คลิป — พร้อมใช้ใน Automation Studio";

            if (failed.Count > 0)
            {
                MessageBox.Show(
                    $"นำเข้าสำเร็จ {imported.Count} คลิป\n\nไฟล์ที่ข้าม:\n{string.Join(Environment.NewLine, failed.Take(10))}",
                    "Import เสร็จพร้อมคำเตือน",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception exception)
        {
            ShowError("Import หลายคลิปไม่สำเร็จ", exception);
        }
    }

    private async void MobileControl_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            MessageBox.Show("กรุณาสร้างหรือเปิด Project ก่อน", "Mobile Control");
            return;
        }

        try
        {
            if (_mobileControlServer is { IsRunning: true })
            {
                var close = MessageBox.Show(
                    $"Mobile Control กำลังทำงาน\n\n{_mobileControlServer.DisplayUrl}\n\nต้องการปิด Server หรือไม่?",
                    "Mobile Control",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);
                if (close == MessageBoxResult.Yes)
                {
                    await _mobileControlServer.StopAsync();
                    _mobileControlServer = null;
                    StatusBarText.Text = "ปิด Mobile Control แล้ว";
                }
                return;
            }

            MobileControlServer? server = null;
            server = new MobileControlServer(
                () => _project,
                () => _jobRepository.ListJobsAsync(
                    _project ?? throw new InvalidOperationException("Project ถูกปิดแล้ว")),
                async workflowId =>
                {
                    if (_project is null)
                    {
                        throw new InvalidOperationException("Project ถูกปิดแล้ว");
                    }

                    var repository = new VisualWorkflowRepository();
                    var workflow = await repository.OpenAsync(_project.RootPath, workflowId)
                                   ?? throw new FileNotFoundException("ไม่พบ Workflow ที่บันทึกไว้");
                    var result = await new VisualWorkflowExecutor().ExecuteAsync(
                        _project,
                        workflow,
                        selectedMedia: _project.SourceMedia,
                        requestApproval: request => server.RequestApprovalAsync(request));
                    _project = result.UpdatedProject;
                    await RunOnUiThreadAsync(async () =>
                    {
                        await RefreshJobsAsync();
                        await TryStartNextQueuedJobAsync();
                        StatusBarText.Text = $"Mobile Automation สร้าง {result.CreatedJobs.Count} Job";
                    });
                    return result;
                },
                filePath => RunOnUiThreadAsync(() => ImportMediaPathsAsync([filePath])));

            _mobileControlServer = server;
            await server.StartAsync();
            StatusBarText.Text = $"Mobile Control พร้อม: {server.DisplayUrl}";
            MessageBox.Show(
                $"เปิดลิงก์นี้บนมือถือที่อยู่ Wi-Fi เดียวกัน\n\n{server.DisplayUrl}\n\nรหัสเชื่อมต่อ: {server.AccessToken}\n\nคอมพิวเตอร์ต้องเปิดโปรแกรมไว้ตลอดการประมวลผล",
                "Mobile Control พร้อมใช้งาน",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            if (_mobileControlServer is not null)
            {
                await _mobileControlServer.DisposeAsync();
            }
            _mobileControlServer = null;
            ShowError("เปิด Mobile Control ไม่สำเร็จ", exception);
        }
    }

    private Task RunOnUiThreadAsync(Func<Task> action)
    {
        if (Dispatcher.CheckAccess())
        {
            return action();
        }
        return Dispatcher.InvokeAsync(action).Task.Unwrap();
    }
}
