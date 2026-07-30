using System.Windows;
using System.Windows.Controls;
using AutoCutStudio.Infrastructure;

namespace AutoCutStudio.App;

public partial class MainWindow
{
    private bool _advancedAiUiEnabled;

    public void EnableAdvancedAiUi()
    {
        if (_advancedAiUiEnabled) return;
        _advancedAiUiEnabled = true;
        if (CommandTextBox.Parent is DockPanel panel)
        {
            var button = new Button
            {
                Content = "Advanced AI / Privacy / Document Video",
                Background = System.Windows.Media.Brushes.DarkSlateGray,
                ToolTip = "YOLO Object, YuNet Face Privacy, B-roll และ Document-to-Video แบบ Local"
            };
            button.Click += AdvancedAiButton_Click;
            panel.Children.Add(button);
        }
    }

    private async void AdvancedAiButton_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null || _activeMedia is null)
        {
            MessageBox.Show("กรุณาเปิด Project และ Import MP4 ก่อน", "AutoCut Studio");
            return;
        }
        RefreshToolStatus();
        if (!_toolAvailability.IsReady)
        {
            MessageBox.Show(_toolAvailability.Message, "FFmpeg Dependency ไม่พร้อม");
            return;
        }

        var transcript = await new TranscriptRepository().OpenAsync(_project.RootPath);
        var window = new AdvancedAiWindow(
            _project,
            _activeMedia,
            transcript,
            _toolLocator,
            _jobRepository,
            PublishProjectEventAsync)
        {
            Owner = this
        };
        _ = window.ShowDialog();
        if (window.CreatedJobs.Count == 0)
        {
            StatusBarText.Text = "ปิด Advanced AI โดยยังไม่สร้าง Job";
            return;
        }

        var history = _project.JobHistory.ToList();
        history.AddRange(window.CreatedJobs.Select(job => job.JobId));
        _project = _project with { JobHistory = history };
        await _projectRepository.SaveAsync(_project);
        MainTabs.SelectedIndex = 2;
        await RefreshJobsAsync();
        await TryStartNextQueuedJobAsync();
        StatusBarText.Text = $"เพิ่ม Advanced AI Job {window.CreatedJobs.Count} งานเข้าคิวแล้ว";
    }
}
