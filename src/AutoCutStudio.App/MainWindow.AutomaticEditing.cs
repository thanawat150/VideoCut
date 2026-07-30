using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;

namespace AutoCutStudio.App;

public partial class MainWindow
{
    private bool _automaticEditingUiEnabled;

    public void EnableAutomaticEditingUi()
    {
        if (_automaticEditingUiEnabled)
        {
            return;
        }

        _automaticEditingUiEnabled = true;
        CommandTextBox.IsReadOnly = false;
        CommandTextBox.Text = "ตัดช่วงเงียบออก";
        CommandTextBox.ToolTip =
            "รองรับคำสั่งตัดหรือลบช่วงเงียบในรุ่นนี้ กด Enter หรือปุ่มวิเคราะห์และตัดอัตโนมัติ";
        CommandTextBox.KeyDown += AutomaticCommandTextBox_KeyDown;

        if (CommandTextBox.Parent is DockPanel commandPanel)
        {
            var automaticButton = new Button
            {
                Content = "วิเคราะห์และตัดอัตโนมัติ",
                Background = System.Windows.Media.Brushes.Teal,
                ToolTip = "ตรวจช่วงเงียบด้วย FFmpeg สร้างแผนให้ตรวจสอบ แล้วเลือกใช้หรือ Export"
            };
            automaticButton.Click += AutomaticEditButton_Click;
            commandPanel.Children.Add(automaticButton);
        }

        StatusBarText.Text = "Automatic Editing พร้อมใช้งาน: รองรับการตัดช่วงเงียบจากเสียงจริง";
    }

    private async void AutomaticCommandTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await OpenAutomaticEditingAsync();
    }

    private async void AutomaticEditButton_Click(object sender, RoutedEventArgs e)
    {
        await OpenAutomaticEditingAsync();
    }

    private async Task OpenAutomaticEditingAsync()
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
            MessageBox.Show(
                _toolAvailability.Message,
                "Dependency ไม่พร้อม",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var parsed = new AutomaticCommandParser().Parse(CommandTextBox.Text);
        if (!parsed.IsSupported)
        {
            MessageBox.Show(
                parsed.Message,
                "คำสั่งยังไม่รองรับ",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new AutomaticEditingWindow(
            _activeMedia.SourcePath,
            _activeMedia.Metadata.DurationSeconds,
            _activeMedia.Metadata.HasAudio,
            _project.ProjectId,
            _toolLocator,
            PublishProjectEventAsync)
        {
            Owner = this
        };
        dialog.CommandTextBox.Text = CommandTextBox.Text;

        var accepted = dialog.ShowDialog() == true;
        if (!accepted || dialog.SelectedPlan is not { IsActionable: true } plan)
        {
            StatusBarText.Text = "ยังไม่ได้เปลี่ยน Timeline จากระบบตัดต่ออัตโนมัติ";
            return;
        }

        try
        {
            PushUndo();
            RestoreSegments(plan.ProposedSegments);
            await AutoSaveTimelineAsync();

            var reportDirectory = Path.Combine(_project.RootPath, "reports");
            Directory.CreateDirectory(reportDirectory);
            var reportPath = Path.Combine(reportDirectory, $"automatic-edit-{plan.PlanId:N}.json");
            await File.WriteAllTextAsync(
                reportPath,
                JsonSerializer.Serialize(plan, JsonDefaults.Options));

            await PublishProjectEventAsync(new AgentEvent
            {
                EventType = "automatic_edit.applied",
                ProjectId = _project.ProjectId,
                AgentId = AgentIds.VideoEditor,
                Action = plan.Action,
                Status = AgentStatuses.Completed,
                Progress = 100,
                Message =
                    $"ใช้แผนตัดช่วงเงียบแล้ว: ลด {plan.RemovedDurationSeconds:0.00} วินาที เหลือ {plan.ProposedSegments.Count} Segment",
                InputPath = plan.InputPath,
                OutputPath = reportPath,
                Metadata = new Dictionary<string, string>
                {
                    ["plan_id"] = plan.PlanId.ToString("N"),
                    ["source_seconds"] = plan.SourceDurationSeconds.ToString("0.###"),
                    ["output_seconds"] = plan.OutputDurationSeconds.ToString("0.###"),
                    ["removed_seconds"] = plan.RemovedDurationSeconds.ToString("0.###"),
                    ["preset"] = plan.Options.PresetId
                }
            });

            MainTabs.SelectedIndex = 1;
            StatusBarText.Text = dialog.ShouldExport
                ? "ใช้แผนอัตโนมัติแล้ว และกำลังสร้าง Export Job"
                : "ใช้แผนอัตโนมัติกับ Timeline แล้ว สามารถ Undo ได้";

            if (dialog.ShouldExport)
            {
                CreateExportJob_Click(this, new RoutedEventArgs());
            }
        }
        catch (Exception exception)
        {
            ShowError("ใช้แผนตัดต่ออัตโนมัติไม่สำเร็จ", exception);
        }
    }
}
