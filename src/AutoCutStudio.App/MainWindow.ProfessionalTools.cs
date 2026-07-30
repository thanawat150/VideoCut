using System.Windows;
using System.Windows.Controls;
using AutoCutStudio.Core.Models;

namespace AutoCutStudio.App;

public partial class MainWindow
{
    private bool _professionalToolsUiEnabled;

    public void EnableProfessionalToolsUi()
    {
        if (_professionalToolsUiEnabled) return;
        _professionalToolsUiEnabled = true;
        if (CommandTextBox.Parent is DockPanel panel)
        {
            var button = new Button
            {
                Content = "Professional Tools",
                Background = System.Windows.Media.Brushes.DarkSlateBlue,
                ToolTip = "Multicam, Keyframes, Nested Sequence, Plugins, Delivery และ Collaboration"
            };
            button.Click += ProfessionalTools_Click;
            panel.Children.Add(button);
        }
    }

    private void ProfessionalTools_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            MessageBox.Show("กรุณาเปิด Project ก่อน", "AutoCut Studio");
            return;
        }
        var dialog = new ProfessionalToolsWindow(
            _project,
            _activeMedia,
            _segments.ToList(),
            _jobRepository,
            _toolLocator,
            RegisterProfessionalJobAsync)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private async Task RegisterProfessionalJobAsync(JobDocument job)
    {
        if (_project is null) throw new InvalidOperationException("Project is not open.");
        var history = _project.JobHistory.ToList();
        history.Add(job.JobId);
        _project = _project with { JobHistory = history };
        await _projectRepository.SaveAsync(_project);
        MainTabs.SelectedIndex = 2;
        await RefreshJobsAsync();
        await TryStartNextQueuedJobAsync();
        StatusBarText.Text = $"เพิ่ม {job.JobType} เข้าคิวแล้ว";
    }
}
