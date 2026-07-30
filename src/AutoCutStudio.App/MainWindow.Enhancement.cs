using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;
using AutoCutStudio.Infrastructure;

namespace AutoCutStudio.App;

public partial class MainWindow
{
    private bool _enhancementUiEnabled;

    public void EnableEnhancementUi()
    {
        if (_enhancementUiEnabled) return;
        _enhancementUiEnabled = true;
        if (CommandTextBox.Parent is DockPanel panel)
        {
            var button = new Button
            {
                Content = "ปรับเสียง / สี / Stabilize",
                Background = System.Windows.Media.Brushes.DarkGoldenrod,
                ToolTip = "ลด Noise, Voice Enhance, Color, Basic Stabilize, Beat และ Music Ducking"
            };
            button.Click += EnhancementButton_Click;
            panel.Children.Add(button);
        }
    }

    private async void EnhancementButton_Click(object sender, RoutedEventArgs e)
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
        if (_segments.Count == 0)
        {
            MessageBox.Show("Timeline ไม่มี Segment สำหรับ Export", "AutoCut Studio");
            return;
        }

        var dialog = new EnhancementWindow(
            _activeMedia.SourcePath,
            _activeMedia.Metadata.DurationSeconds,
            _toolLocator)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true || dialog.Plan is null) return;

        try
        {
            PathSecurity.ValidateTimelineSegments(_segments, _activeMedia.Metadata.DurationSeconds);
            var job = await new EnhancementJobFactory(_jobRepository).CreateAsync(
                _project,
                _activeMedia,
                _segments.ToList(),
                dialog.Plan);
            var history = _project.JobHistory.ToList();
            history.Add(job.JobId);
            _project = _project with { JobHistory = history };
            await _projectRepository.SaveAsync(_project);

            var reportDirectory = Path.Combine(_project.RootPath, "reports");
            Directory.CreateDirectory(reportDirectory);
            var planPath = Path.Combine(reportDirectory, $"enhancement-plan-{dialog.Plan.PlanId:N}.json");
            await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(dialog.Plan, JsonDefaults.Options));
            if (dialog.BeatAnalysis is not null)
            {
                var beatPath = Path.Combine(reportDirectory, $"beat-analysis-{dialog.Plan.PlanId:N}.json");
                await File.WriteAllTextAsync(beatPath, JsonSerializer.Serialize(dialog.BeatAnalysis, JsonDefaults.Options));
            }

            var agentEvent = new AgentEvent
            {
                EventType = "enhancement.job_created",
                ProjectId = _project.ProjectId,
                JobId = job.JobId,
                AgentId = AgentIds.Producer,
                Action = "create_enhancement_job",
                Status = AgentStatuses.Completed,
                Progress = 100,
                Message = "สร้าง Workflow: Timeline → Audio/Visual Enhancement → QA",
                InputPath = job.InputPath,
                OutputPath = job.OutputPath,
                Metadata = new Dictionary<string, string>
                {
                    ["audio_preset"] = dialog.Plan.AudioPreset,
                    ["color_preset"] = dialog.Plan.ColorPreset,
                    ["stabilize"] = dialog.Plan.Stabilize.ToString(),
                    ["music_ducking"] = dialog.Plan.EnableMusicDucking.ToString()
                }
            };
            await _jobRepository.AppendEventAsync(job, agentEvent);
            _eventBus.Publish(agentEvent);
            MainTabs.SelectedIndex = 2;
            await RefreshJobsAsync();
            await TryStartNextQueuedJobAsync();
            StatusBarText.Text = "เพิ่ม Enhancement Job เข้าคิวแล้ว";
        }
        catch (Exception exception)
        {
            ShowError("สร้าง Enhancement Job ไม่สำเร็จ", exception);
        }
    }
}
