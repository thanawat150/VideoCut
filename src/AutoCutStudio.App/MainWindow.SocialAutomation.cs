using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;
using AutoCutStudio.Infrastructure;

namespace AutoCutStudio.App;

public partial class MainWindow
{
    private bool _socialAutomationUiEnabled;

    public void EnableSocialAutomationUi()
    {
        if (_socialAutomationUiEnabled)
        {
            return;
        }

        _socialAutomationUiEnabled = true;
        if (CommandTextBox.Parent is DockPanel commandPanel)
        {
            var button = new Button
            {
                Content = "สร้าง Highlights / Shorts",
                Background = System.Windows.Media.Brushes.DarkMagenta,
                ToolTip = "วิเคราะห์ Transcript เสนอ Highlight แล้วสร้างคลิปตามแพลตฟอร์ม"
            };
            button.Click += SocialAutomationButton_Click;
            commandPanel.Children.Add(button);
        }
    }

    private async void SocialAutomationButton_Click(object sender, RoutedEventArgs e)
    {
        await OpenSocialAutomationAsync();
    }

    private async Task OpenSocialAutomationAsync()
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

        var transcriptRepository = new TranscriptRepository();
        var transcript = await transcriptRepository.OpenAsync(_project.RootPath);
        if (transcript is null || transcript.MediaAssetId != _activeMedia.Id)
        {
            MessageBox.Show(
                "ยังไม่มี Transcript สำหรับวิดีโอนี้\n\nกด ‘ถอดเสียง / Subtitle’ ก่อน เพื่อให้ระบบใช้ข้อความและ Timestamp จริงในการเสนอ Highlight",
                "ต้องมี Transcript",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new SocialAutomationWindow(
            transcript,
            _activeMedia.Metadata.DurationSeconds,
            PreviewTranscriptSegment)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true || dialog.Plan is null)
        {
            StatusBarText.Text = "ยกเลิกการสร้าง Social Clips";
            return;
        }

        var plan = dialog.Plan;
        try
        {
            var assBuilder = new AssCaptionBuilder();
            var createdJobs = new List<JobDocument>();
            foreach (var candidate in plan.Candidates.Take(3))
            {
                var ass = assBuilder.Build(
                    transcript,
                    candidate,
                    plan.Preset,
                    plan.HookText,
                    plan.CtaText);
                var job = await _jobRepository.CreateSocialClipJobAsync(
                    _project,
                    _activeMedia,
                    candidate,
                    plan.Preset,
                    ass,
                    plan.HookText,
                    plan.CtaText,
                    plan.BurnCaptions,
                    priority: 10 + candidate.Rank);
                var createdEvent = new AgentEvent
                {
                    EventType = "social_clip.job_created",
                    ProjectId = _project.ProjectId,
                    JobId = job.JobId,
                    AgentId = AgentIds.Producer,
                    Action = "create_social_clip",
                    Status = AgentStatuses.Completed,
                    Progress = 100,
                    Message = $"สร้าง Job {plan.Preset.DisplayName} อันดับ {candidate.Rank} ({candidate.DurationSeconds:0.0} วินาที)",
                    InputPath = job.InputPath,
                    OutputPath = job.OutputPath,
                    Metadata = new Dictionary<string, string>
                    {
                        ["highlight_score"] = candidate.Score.ToString("0.0"),
                        ["preset"] = plan.Preset.Id,
                        ["resolution"] = $"{plan.Preset.Width}x{plan.Preset.Height}",
                        ["reframe"] = plan.Preset.AspectStrategy
                    }
                };
                await _jobRepository.AppendEventAsync(job, createdEvent);
                _eventBus.Publish(createdEvent);
                createdJobs.Add(job);
            }

            var history = _project.JobHistory.ToList();
            history.AddRange(createdJobs.Select(job => job.JobId));
            _project = _project with { JobHistory = history };
            await _projectRepository.SaveAsync(_project);

            var reportDirectory = Path.Combine(_project.RootPath, "reports");
            Directory.CreateDirectory(reportDirectory);
            var planPath = Path.Combine(reportDirectory, $"social-plan-{plan.PlanId:N}.json");
            await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan, JsonDefaults.Options));

            MainTabs.SelectedIndex = 2;
            await RefreshJobsAsync();
            await TryStartNextQueuedJobAsync();
            StatusBarText.Text = $"เพิ่ม Social Clip {createdJobs.Count} งานเข้าคิวแล้ว";
        }
        catch (Exception exception)
        {
            ShowError("สร้าง Social Clip Jobs ไม่สำเร็จ", exception);
        }
    }
}
