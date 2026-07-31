using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace AutoCutStudio.Rebuild;

public sealed partial class MainWindow : Window
{
    private readonly TimelineEditor _editor = new();
    private readonly SafeProcess _runner = new();
    private readonly FfprobeService _probe;
    private readonly ListBox _media = new();
    private readonly DataGrid _timeline = new() { AutoGenerateColumns = true, IsReadOnly = true };
    private readonly MediaElement _preview = new() { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, Stretch = Stretch.Uniform };
    private readonly TextBlock _status = new() { Text = "พร้อม", VerticalAlignment = VerticalAlignment.Center };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, Width = 260, Height = 18 };
    private readonly TextBox _input = new() { Text = "0" };
    private readonly TextBox _output = new() { Text = "0" };
    private ProjectDocument? _project;
    private Process? _workerProcess;
    private JobDocument? _runningJob;

    public MainWindow()
    {
        _probe = new FfprobeService(_runner);
        Title = "AutoCut Studio Rebuild"; Width = 1280; Height = 780; MinWidth = 1000; MinHeight = 650; Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
        Content = BuildUi();
        _media.SelectionChanged += (_, _) => { if (_media.SelectedItem is MediaAsset item) { _preview.Stop(); _preview.Source = new Uri(item.SourcePath); } };
    }

    private UIElement BuildUi()
    {
        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(220) }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
        toolbar.Children.Add(Button("สร้างโปรเจกต์", NewProject)); toolbar.Children.Add(Button("เปิดโปรเจกต์", OpenProject)); toolbar.Children.Add(Button("บันทึก", SaveProject)); toolbar.Children.Add(Button("นำเข้าวิดีโอ", Import)); toolbar.Children.Add(Button("Export MP4", Export)); toolbar.Children.Add(Button("ยกเลิกงาน", CancelJob));
        Grid.SetRow(toolbar, 0); root.Children.Add(toolbar);
        var workspace = new Grid { Margin = new Thickness(0, 8, 0, 8) }; workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) }); workspace.ColumnDefinitions.Add(new ColumnDefinition()); workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        workspace.Children.Add(Group("คลังสื่อ", _media, 0));
        var previewGrid = new Grid { Background = Brushes.Black }; previewGrid.Children.Add(_preview); var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Background = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)) }; controls.Children.Add(Button("▶", (_, _) => _preview.Play())); controls.Children.Add(Button("⏸", (_, _) => _preview.Pause())); controls.Children.Add(Button("⏹", (_, _) => _preview.Stop())); previewGrid.Children.Add(controls); workspace.Children.Add(Group("Preview", previewGrid, 1));
        var automation = new StackPanel(); automation.Children.Add(new TextBlock { Text = "Phase 2–6 ใช้ Backend จริงเมื่อ Dependency พร้อม", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6) });
        automation.Children.Add(Button("ตรวจช่วงเงียบ", async (_, _) => await RunSelected(JobKind.DetectSilence, ".txt"))); automation.Children.Add(Button("ถอดเสียง Whisper", Transcribe)); automation.Children.Add(Button("ฝัง Subtitle", BurnSubtitle)); automation.Children.Add(Button("สร้าง Shorts 9:16", async (_, _) => await RunSelected(JobKind.CreateShort, ".mp4"))); automation.Children.Add(Button("ทำเสียงพูดให้ชัด", async (_, _) => await RunSelected(JobKind.EnhanceAudio, ".mp4"))); automation.Children.Add(Button("ใส่เสียงพากย์", MixVoiceover)); automation.Children.Add(Button("Stabilize", async (_, _) => await RunSelected(JobKind.Stabilize, ".mp4"))); automation.Children.Add(Button("ใส่ Logo", OverlayLogo)); automation.Children.Add(Button("Privacy Blur", async (_, _) => await RunSelected(JobKind.PrivacyBlur, ".mp4")));
        workspace.Children.Add(Group("เครื่องมืออัตโนมัติ", new ScrollViewer { Content = automation }, 2)); Grid.SetRow(workspace, 1); root.Children.Add(workspace);
        var edit = new Grid(); edit.ColumnDefinitions.Add(new ColumnDefinition()); edit.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) }); edit.Children.Add(_timeline); var panel = new StackPanel { Margin = new Thickness(8) }; panel.Children.Add(new TextBlock { Text = "In (วินาที)" }); panel.Children.Add(_input); panel.Children.Add(new TextBlock { Text = "Out (วินาที)" }); panel.Children.Add(_output); panel.Children.Add(Button("Trim", Trim)); panel.Children.Add(Button("Split ที่ Out", Split)); panel.Children.Add(Button("ลบ", Delete)); panel.Children.Add(Button("เลื่อนขึ้น", async (_, _) => await Move(-1))); panel.Children.Add(Button("เลื่อนลง", async (_, _) => await Move(1))); Grid.SetColumn(panel, 1); edit.Children.Add(panel); var timelineGroup = new GroupBox { Header = "Timeline", Content = edit, Margin = new Thickness(4) }; Grid.SetRow(timelineGroup, 2); root.Children.Add(timelineGroup);
        var footer = new DockPanel { Margin = new Thickness(4) }; DockPanel.SetDock(_progress, Dock.Right); footer.Children.Add(_progress); footer.Children.Add(_status); Grid.SetRow(footer, 3); root.Children.Add(footer); return root;
    }

    private static Button Button(string text, RoutedEventHandler handler) { var button = new Button { Content = text, Margin = new Thickness(4), Padding = new Thickness(12, 7, 12, 7), MinHeight = 34 }; button.Click += handler; return button; }
    private static GroupBox Group(string title, UIElement content, int column) { var box = new GroupBox { Header = title, Content = content, Margin = new Thickness(4) }; Grid.SetColumn(box, column); return box; }
}
