using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;
using AutoCutStudio.Infrastructure;

namespace AutoCutStudio.App;

public partial class MainWindow
{
    private readonly SpeechToolLocator _speechToolLocator = new();
    private bool _speechEditingUiEnabled;
    private DispatcherTimer? _speechPreviewTimer;
    private double? _speechPreviewEndSeconds;

    public void EnableSpeechEditingUi()
    {
        if (_speechEditingUiEnabled)
        {
            return;
        }

        _speechEditingUiEnabled = true;
        if (CommandTextBox.Parent is DockPanel commandPanel)
        {
            var speechButton = new Button
            {
                Content = "ถอดเสียง / Subtitle",
                Background = System.Windows.Media.Brushes.DarkSlateBlue,
                ToolTip = "ถอดเสียงด้วย Whisper Local แก้ Transcript ค้นหา Filler และ Export SRT"
            };
            speechButton.Click += SpeechEditingButton_Click;
            commandPanel.Children.Add(speechButton);
        }

        _speechPreviewTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _speechPreviewTimer.Tick += (_, _) =>
        {
            if (_speechPreviewEndSeconds is not null &&
                PreviewPlayer.Position.TotalSeconds >= _speechPreviewEndSeconds.Value)
            {
                PreviewPlayer.Pause();
                _speechPreviewEndSeconds = null;
                _speechPreviewTimer.Stop();
            }
        };
    }

    private async void SpeechEditingButton_Click(object sender, RoutedEventArgs e)
    {
        await OpenSpeechEditingAsync(AutomaticEditActions.TranscribeSpeech, "ถอดเสียง");
    }

    private async Task OpenSpeechEditingAsync(string? requestedAction, string originalCommand)
    {
        if (_project is null || _activeMedia is null)
        {
            MessageBox.Show(
                "กรุณาสร้างหรือเปิด Project แล้ว Import MP4 ก่อน",
                "AutoCut Studio",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        RefreshToolStatus();
        if (!_toolAvailability.IsReady)
        {
            MessageBox.Show(_toolAvailability.Message, "FFmpeg Dependency ไม่พร้อม");
            return;
        }

        var speechAvailability = _speechToolLocator.Locate();
        if (!speechAvailability.IsReady)
        {
            MessageBox.Show(
                speechAvailability.Message,
                "Whisper Dependency ไม่พร้อม",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        await PublishProjectEventAsync(new AgentEvent
        {
            EventType = "speech_editor.opened",
            ProjectId = _project.ProjectId,
            AgentId = AgentIds.Producer,
            Action = requestedAction ?? AutomaticEditActions.TranscribeSpeech,
            Status = AgentStatuses.WaitingForUser,
            Message = $"เปิด Speech Editing ตามคำสั่ง: {originalCommand}",
            InputPath = _activeMedia.SourcePath
        });

        var dialog = new TranscriptWindow(
            _project.RootPath,
            AppPaths.CacheDirectory,
            _project.ProjectId,
            _activeMedia,
            _toolLocator,
            _speechToolLocator,
            PublishProjectEventAsync,
            PreviewTranscriptSegment)
        {
            Owner = this
        };

        if (requestedAction == AutomaticEditActions.RemoveFillerWords)
        {
            dialog.Title = "Speech Editing — ตรวจและลบ Filler เดี่ยว";
        }
        else if (requestedAction == AutomaticEditActions.CreateSubtitles)
        {
            dialog.Title = "Speech Editing — สร้างและแก้ Subtitle SRT";
        }

        var accepted = dialog.ShowDialog() == true;
        if (!accepted || !dialog.ApplyTimelineRequested || dialog.Transcript is null)
        {
            StatusBarText.Text = "ปิด Speech Editor โดยยังไม่เปลี่ยน Timeline";
            return;
        }

        try
        {
            var excludedCount = dialog.Transcript.Segments.Count(item => item.IsExcluded);
            if (excludedCount == 0)
            {
                MessageBox.Show(
                    "ยังไม่มีช่วง Transcript ที่ทำเครื่องหมาย ‘ตัดออก’ Timeline จึงไม่เปลี่ยน",
                    "AutoCut Studio");
                return;
            }

            var editor = new TranscriptEditingService();
            var proposed = editor.BuildTimelineWithoutExcluded(
                dialog.Transcript,
                _activeMedia.Metadata.DurationSeconds);
            PathSecurity.ValidateTimelineSegments(proposed, _activeMedia.Metadata.DurationSeconds);

            PushUndo();
            RestoreSegments(proposed);
            await AutoSaveTimelineAsync();
            MainTabs.SelectedIndex = 1;

            await PublishProjectEventAsync(new AgentEvent
            {
                EventType = "transcript.timeline_applied",
                ProjectId = _project.ProjectId,
                AgentId = AgentIds.VideoEditor,
                Action = "remove_excluded_transcript_segments",
                Status = AgentStatuses.Completed,
                Progress = 100,
                Message = $"ตัดช่วงจาก Transcript {excludedCount} ช่วง เหลือ Timeline {proposed.Count} Segment",
                InputPath = _activeMedia.SourcePath,
                OutputPath = Path.Combine(_project.RootPath, "transcript", "transcript.json"),
                Metadata = new Dictionary<string, string>
                {
                    ["excluded_segments"] = excludedCount.ToString(),
                    ["timeline_segments"] = proposed.Count.ToString()
                }
            });

            StatusBarText.Text = "ใช้ Transcript กับ Timeline แล้ว สามารถ Undo ได้";
            if (MessageBox.Show(
                    "ใช้ Transcript กับ Timeline แล้ว ต้องการสร้าง Export Job ตอนนี้หรือไม่?",
                    "AutoCut Studio",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                CreateExportJob_Click(this, new RoutedEventArgs());
            }
        }
        catch (Exception exception)
        {
            ShowError("ใช้ Transcript กับ Timeline ไม่สำเร็จ", exception);
        }
    }

    private void PreviewTranscriptSegment(double startSeconds, double endSeconds)
    {
        if (PreviewPlayer.Source is null)
        {
            return;
        }

        MainTabs.SelectedIndex = 1;
        PreviewPlayer.Position = TimeSpan.FromSeconds(Math.Max(0, startSeconds));
        _speechPreviewEndSeconds = Math.Max(startSeconds, endSeconds);
        PreviewPlayer.Play();
        _speechPreviewTimer?.Start();
    }
}
