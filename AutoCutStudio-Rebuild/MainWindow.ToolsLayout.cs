using System.Windows;
using System.Windows.Controls;
using WpfGroupBox = System.Windows.Controls.GroupBox;

namespace AutoCutStudio.Rebuild;

public sealed partial class MainWindow
{
    private UIElement BuildAutomation()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Phase 2–6 ใช้ Backend จริงเมื่อ Dependency พร้อม",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(6)
        });
        panel.Children.Add(CreateButton("ตรวจช่วงเงียบ", async (_, _) => await RunSelected(JobKind.DetectSilence, ".txt")));
        panel.Children.Add(CreateButton("ถอดเสียง Whisper", Transcribe));
        panel.Children.Add(CreateButton("ฝัง Subtitle", BurnSubtitle));
        panel.Children.Add(CreateButton("สร้าง Shorts 9:16", async (_, _) => await RunSelected(JobKind.CreateShort, ".mp4")));
        panel.Children.Add(CreateButton("ทำเสียงพูดให้ชัด", async (_, _) => await RunSelected(JobKind.EnhanceAudio, ".mp4")));
        panel.Children.Add(CreateButton("ใส่เสียงพากย์", MixVoiceover));
        panel.Children.Add(CreateButton("Stabilize", async (_, _) => await RunSelected(JobKind.Stabilize, ".mp4")));
        panel.Children.Add(CreateButton("ใส่ Logo", OverlayLogo));
        panel.Children.Add(CreateButton("Privacy Blur", async (_, _) => await RunSelected(JobKind.PrivacyBlur, ".mp4")));
        return new ScrollViewer { Content = panel };
    }

    private void AddTimeline(Grid root)
    {
        var edit = new Grid();
        edit.ColumnDefinitions.Add(new ColumnDefinition());
        edit.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        edit.Children.Add(_timeline);

        var panel = new StackPanel { Margin = new Thickness(8) };
        panel.Children.Add(new TextBlock { Text = "In (วินาที)" });
        panel.Children.Add(_input);
        panel.Children.Add(new TextBlock { Text = "Out (วินาที)" });
        panel.Children.Add(_output);
        panel.Children.Add(CreateButton("Trim", Trim));
        panel.Children.Add(CreateButton("Split ที่ Out", Split));
        panel.Children.Add(CreateButton("ลบ", Delete));
        panel.Children.Add(CreateButton("เลื่อนขึ้น", async (_, _) => await Move(-1)));
        panel.Children.Add(CreateButton("เลื่อนลง", async (_, _) => await Move(1)));
        Grid.SetColumn(panel, 1);
        edit.Children.Add(panel);

        var group = new WpfGroupBox
        {
            Header = "Timeline",
            Content = edit,
            Margin = new Thickness(4)
        };
        Grid.SetRow(group, 2);
        root.Children.Add(group);
    }

    private void AddFooter(Grid root)
    {
        var footer = new DockPanel { Margin = new Thickness(4) };
        DockPanel.SetDock(_progress, Dock.Right);
        footer.Children.Add(_progress);
        footer.Children.Add(_status);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);
    }
}
