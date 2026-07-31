using System.Windows;
using System.Windows.Controls;
using AutoCutStudio.Core.Models;

namespace AutoCutStudio.App;

public partial class MainWindow
{
    private bool _scriptVideoUiEnabled;

    public void EnableScriptVideoUi()
    {
        if (_scriptVideoUiEnabled)
            return;
        _scriptVideoUiEnabled = true;

        if (CommandTextBox.Parent is not DockPanel commandPanel)
            return;

        var button = new Button
        {
            Content = "✦ Script to Video",
            ToolTip = "ใส่สคริปต์แล้วสร้างเสียง ฉาก ภาพประกอบ ซับ และวิดีโออัตโนมัติ"
        };
        button.SetResourceReference(StyleProperty, "PrimaryButtonStyle");
        button.Click += ScriptVideo_Click;
        commandPanel.Children.Add(button);
    }

    private async void ScriptVideo_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            MessageBox.Show("กรุณาสร้างหรือเปิด Project ก่อน", "Script to Video");
            return;
        }

        var window = new ScriptVideoWindow(_project, _jobRepository)
        {
            Owner = this
        };
        if (window.ShowDialog() != true || window.CreatedJob is null)
            return;

        var job = window.CreatedJob;
        var history = _project.JobHistory.ToList();
        if (!history.Contains(job.JobId))
            history.Add(job.JobId);
        _project = _project with
        {
            JobHistory = history,
            ModifiedAt = DateTimeOffset.UtcNow
        };
        await _projectRepository.SaveAsync(_project);
        await RefreshJobsAsync();
        await TryStartNextQueuedJobAsync();
        MainTabs.SelectedIndex = 2;
        StatusBarText.Text = $"Script-to-Video Job เริ่มทำงานแล้ว: {job.JobId:N}";

        await PublishProjectEventAsync(new AgentEvent
        {
            EventType = "script_video.job_created",
            ProjectId = _project.ProjectId,
            JobId = job.JobId,
            AgentId = AgentIds.Producer,
            Action = job.JobType,
            Status = AgentStatuses.Completed,
            Progress = 100,
            Message = "สร้าง Script-to-Video Job พร้อมเสียงและภาพประกอบแล้ว",
            InputPath = job.InputPath,
            OutputPath = job.OutputPath
        });
    }
}
