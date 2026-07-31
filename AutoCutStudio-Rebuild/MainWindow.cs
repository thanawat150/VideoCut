using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfButton = System.Windows.Controls.Button;
using WpfGroupBox = System.Windows.Controls.GroupBox;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfProgressBar = System.Windows.Controls.ProgressBar;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace AutoCutStudio.Rebuild;

public sealed partial class MainWindow : Window
{
    private readonly TimelineEditor _editor = new();
    private readonly SafeProcess _runner = new();
    private readonly FfprobeService _probe;
    private readonly WpfListBox _media = new();
    private readonly DataGrid _timeline = new()
    {
        AutoGenerateColumns = true,
        IsReadOnly = true
    };
    private readonly MediaElement _preview = new()
    {
        LoadedBehavior = MediaState.Manual,
        UnloadedBehavior = MediaState.Manual,
        Stretch = Stretch.Uniform
    };
    private readonly TextBlock _status = new()
    {
        Text = "พร้อม",
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly WpfProgressBar _progress = new()
    {
        Minimum = 0,
        Maximum = 100,
        Width = 260,
        Height = 18
    };
    private readonly WpfTextBox _input = new() { Text = "0" };
    private readonly WpfTextBox _output = new() { Text = "0" };

    private ProjectDocument? _project;
    private Process? _workerProcess;
    private JobDocument? _runningJob;

    public MainWindow()
    {
        _probe = new FfprobeService(_runner);
        Title = "AutoCut Studio Rebuild";
        Width = 1280;
        Height = 780;
        MinWidth = 1000;
        MinHeight = 650;
        Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
        Content = BuildUi();

        _media.SelectionChanged += (_, _) =>
        {
            if (_media.SelectedItem is not MediaAsset item)
                return;
            _preview.Stop();
            _preview.Source = new Uri(item.SourcePath);
        };
    }

    private static WpfButton CreateButton(string text, RoutedEventHandler handler)
    {
        var button = new WpfButton
        {
            Content = text,
            Margin = new Thickness(4),
            Padding = new Thickness(12, 7, 12, 7),
            MinHeight = 34
        };
        button.Click += handler;
        return button;
    }

    private static WpfGroupBox CreateGroup(string title, UIElement content, int column)
    {
        var box = new WpfGroupBox
        {
            Header = title,
            Content = content,
            Margin = new Thickness(4)
        };
        Grid.SetColumn(box, column);
        return box;
    }
}
