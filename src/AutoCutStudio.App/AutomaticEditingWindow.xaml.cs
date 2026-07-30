using System.Windows;
using System.Windows.Controls;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;
using AutoCutStudio.Infrastructure;

namespace AutoCutStudio.App;

public partial class AutomaticEditingWindow : Window
{
    private readonly string _inputPath;
    private readonly double _sourceDurationSeconds;
    private readonly bool _hasAudio;
    private readonly Guid _projectId;
    private readonly Func<AgentEvent, Task>? _eventSink;
    private readonly FfmpegSilenceDetector _silenceDetector;
    private readonly AutomaticCommandParser _commandParser = new();
    private readonly AutomaticEditPlanner _planner = new();
    private CancellationTokenSource? _analysisCancellation;

    public AutomaticEditingWindow(
        string inputPath,
        double sourceDurationSeconds,
        bool hasAudio,
        Guid projectId,
        ToolLocator toolLocator,
        Func<AgentEvent, Task>? eventSink)
    {
        InitializeComponent();
        _inputPath = inputPath;
        _sourceDurationSeconds = sourceDurationSeconds;
        _hasAudio = hasAudio;
        _projectId = projectId;
        _eventSink = eventSink;
        _silenceDetector = new FfmpegSilenceDetector(toolLocator);
        Loaded += AutomaticEditingWindow_Loaded;
        Closing += AutomaticEditingWindow_Closing;
    }

    public AutomaticEditPlan? SelectedPlan { get; private set; }
    public bool ShouldExport { get; private set; }

    private async void AutomaticEditingWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await AnalyzeAsync();
    }

    private void AutomaticEditingWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _analysisCancellation?.Cancel();
        _analysisCancellation?.Dispose();
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        await AnalyzeAsync();
    }

    private async Task AnalyzeAsync()
    {
        _analysisCancellation?.Cancel();
        _analysisCancellation?.Dispose();
        _analysisCancellation = new CancellationTokenSource();

        SelectedPlan = null;
        ApplyButton.IsEnabled = false;
        ApplyAndExportButton.IsEnabled = false;
        SilenceGrid.ItemsSource = null;
        ProposedSegmentsGrid.ItemsSource = null;
        WarningsTextBox.Text = string.Empty;

        var request = _commandParser.Parse(CommandTextBox.Text);
        if (!request.IsSupported)
        {
            AnalysisStatusText.Text = request.Message;
            SummaryTextBlock.Text = "คำสั่งนี้ยังไม่รองรับในระบบตัดต่ออัตโนมัติรุ่นปัจจุบัน";
            return;
        }

        if (!_hasAudio)
        {
            AnalysisStatusText.Text = "ไฟล์นี้ไม่มี Audio Stream จึงตรวจช่วงเงียบไม่ได้";
            SummaryTextBlock.Text = "ไม่มีการเปลี่ยน Timeline";
            return;
        }

        var options = SelectedOptions();
        SetBusy(true, $"กำลังตรวจระดับเสียงจริงด้วย FFmpeg — {options.DisplayName}");

        try
        {
            await EmitAsync(new AgentEvent
            {
                EventType = "automatic_edit.command_accepted",
                ProjectId = _projectId,
                AgentId = AgentIds.Producer,
                Action = AutomaticEditActions.RemoveSilence,
                Status = AgentStatuses.Thinking,
                Message = request.Message,
                InputPath = _inputPath
            });

            await EmitAsync(new AgentEvent
            {
                EventType = "silence_detection.started",
                ProjectId = _projectId,
                AgentId = AgentIds.MediaAnalyst,
                Action = "detect_silence",
                Status = AgentStatuses.Analysing,
                Message = $"กำลังตรวจช่วงเงียบด้วยเกณฑ์ {options.NoiseThresholdDb:0.#} dB / {options.MinimumSilenceSeconds:0.##} วินาที",
                InputPath = _inputPath
            });

            var detection = await _silenceDetector.DetectAsync(
                _inputPath,
                _sourceDurationSeconds,
                options,
                _analysisCancellation.Token);

            var plan = _planner.BuildRemoveSilencePlan(
                request.OriginalCommand,
                _inputPath,
                _sourceDurationSeconds,
                options,
                detection.Intervals,
                detection.SafeCommandDisplay);

            SelectedPlan = plan;
            SilenceGrid.ItemsSource = plan.DetectedSilence;
            ProposedSegmentsGrid.ItemsSource = plan.ProposedSegments;
            WarningsTextBox.Text = plan.Warnings.Count == 0
                ? "ไม่พบคำเตือน\n\nTechnical command:\n" + plan.DetectionCommand
                : string.Join(Environment.NewLine, plan.Warnings.Select(item => "• " + item))
                  + "\n\nTechnical command:\n" + plan.DetectionCommand;

            SummaryTextBlock.Text =
                $"ตรวจพบช่วงเงียบ {plan.DetectedSilence.Count} ช่วง | " +
                $"Timeline เดิม {plan.SourceDurationSeconds:0.00} วินาที → " +
                $"เสนอ {plan.OutputDurationSeconds:0.00} วินาที | " +
                $"ลดลง {plan.RemovedDurationSeconds:0.00} วินาที | " +
                $"เหลือ {plan.ProposedSegments.Count} Segment";
            AnalysisStatusText.Text = plan.IsActionable
                ? "สร้างแผนจากผลวิเคราะห์จริงแล้ว กรุณาตรวจช่วงก่อนนำไปใช้"
                : plan.Warnings.FirstOrDefault() ?? "ยังไม่มีส่วนที่ควรตัด";
            ApplyButton.IsEnabled = plan.IsActionable;
            ApplyAndExportButton.IsEnabled = plan.IsActionable;

            await EmitAsync(new AgentEvent
            {
                EventType = "silence_detection.completed",
                ProjectId = _projectId,
                AgentId = AgentIds.MediaAnalyst,
                Action = "detect_silence",
                Status = AgentStatuses.Completed,
                Progress = 100,
                Message = $"ตรวจพบช่วงเงียบ {plan.DetectedSilence.Count} ช่วง",
                InputPath = _inputPath,
                Metadata = new Dictionary<string, string>
                {
                    ["silence_count"] = plan.DetectedSilence.Count.ToString(),
                    ["removed_seconds"] = plan.RemovedDurationSeconds.ToString("0.###"),
                    ["preset"] = plan.Options.PresetId
                }
            });

            await EmitAsync(new AgentEvent
            {
                EventType = "automatic_edit.plan_ready",
                ProjectId = _projectId,
                AgentId = AgentIds.VideoEditor,
                Action = "build_timeline_plan",
                Status = plan.IsActionable ? AgentStatuses.WaitingForUser : AgentStatuses.Warning,
                Progress = 100,
                Message = plan.IsActionable
                    ? $"แผนพร้อมตรวจสอบ: {plan.ProposedSegments.Count} Segment"
                    : "แผนไม่มีส่วนที่ควรตัด",
                InputPath = _inputPath,
                RequiresUserAction = plan.IsActionable
            });
        }
        catch (OperationCanceledException)
        {
            AnalysisStatusText.Text = "ยกเลิกการวิเคราะห์แล้ว";
        }
        catch (Exception exception)
        {
            AnalysisStatusText.Text = $"วิเคราะห์ไม่สำเร็จ: {exception.Message}";
            SummaryTextBlock.Text = "Source Media ไม่ถูกแก้ไข";
            await EmitAsync(new AgentEvent
            {
                EventType = "automatic_edit.failed",
                ProjectId = _projectId,
                AgentId = AgentIds.MediaAnalyst,
                Action = "detect_silence",
                Status = AgentStatuses.Error,
                Message = exception.Message,
                InputPath = _inputPath,
                Severity = "error",
                RequiresUserAction = true
            });
        }
        finally
        {
            SetBusy(false, AnalysisStatusText.Text);
        }
    }

    private SilenceDetectionOptions SelectedOptions()
    {
        var presetId = (PresetComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "balanced";
        return presetId switch
        {
            "gentle" => new SilenceDetectionOptions
            {
                PresetId = "gentle",
                DisplayName = "นุ่มนวล",
                NoiseThresholdDb = -42,
                MinimumSilenceSeconds = 0.8,
                EdgePaddingSeconds = 0.16,
                MinimumOutputSegmentSeconds = 0.12
            },
            "aggressive" => new SilenceDetectionOptions
            {
                PresetId = "aggressive",
                DisplayName = "กระชับ",
                NoiseThresholdDb = -30,
                MinimumSilenceSeconds = 0.3,
                EdgePaddingSeconds = 0.08,
                MinimumOutputSegmentSeconds = 0.06
            },
            _ => new SilenceDetectionOptions
            {
                PresetId = "balanced",
                DisplayName = "สมดุล",
                NoiseThresholdDb = -35,
                MinimumSilenceSeconds = 0.5,
                EdgePaddingSeconds = 0.12,
                MinimumOutputSegmentSeconds = 0.08
            }
        };
    }

    private void SetBusy(bool isBusy, string message)
    {
        AnalysisProgress.IsIndeterminate = isBusy;
        AnalyzeButton.IsEnabled = !isBusy;
        PresetComboBox.IsEnabled = !isBusy;
        CommandTextBox.IsEnabled = !isBusy;
        AnalysisStatusText.Text = message;
    }

    private Task EmitAsync(AgentEvent agentEvent) =>
        _eventSink?.Invoke(agentEvent) ?? Task.CompletedTask;

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        Complete(export: false);
    }

    private void ApplyAndExport_Click(object sender, RoutedEventArgs e)
    {
        Complete(export: true);
    }

    private void Complete(bool export)
    {
        if (SelectedPlan is not { IsActionable: true })
        {
            MessageBox.Show("ยังไม่มีแผนที่นำไปใช้ได้", "AutoCut Studio");
            return;
        }

        ShouldExport = export;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
