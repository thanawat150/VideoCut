using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;

namespace AutoCutStudio.Rebuild;

public sealed class SystemDoctorWindow : Window
{
    private readonly string? _projectRoot;
    private readonly SystemDoctorService _service = new();
    private readonly DataGrid _grid = new()
    {
        AutoGenerateColumns = false,
        IsReadOnly = true,
        CanUserAddRows = false,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        Margin = new Thickness(12)
    };
    private readonly TextBlock _summary = new()
    {
        Margin = new Thickness(12, 0, 12, 8),
        FontSize = 15,
        FontWeight = FontWeights.SemiBold
    };
    private readonly ProgressBar _progress = new()
    {
        IsIndeterminate = true,
        Height = 5,
        Margin = new Thickness(12, 0, 12, 8),
        Visibility = Visibility.Collapsed
    };
    private readonly Button _runButton = new() { Content = "ตรวจใหม่", Margin = new Thickness(4), Padding = new Thickness(14, 7, 14, 7) };
    private readonly Button _exportButton = new() { Content = "Export รายงาน JSON", Margin = new Thickness(4), Padding = new Thickness(14, 7, 14, 7), IsEnabled = false };
    private DoctorReport? _report;

    public SystemDoctorWindow(string? projectRoot)
    {
        _projectRoot = projectRoot;
        Title = "System Doctor — AutoCut Studio Rebuild";
        Width = 1040;
        Height = 700;
        MinWidth = 820;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));

        ConfigureColumns();
        Content = BuildContent();
        Loaded += async (_, _) => await RunDoctorAsync();
        _runButton.Click += async (_, _) => await RunDoctorAsync();
        _exportButton.Click += async (_, _) => await ExportAsync();
    }

    private UIElement BuildContent()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel { Margin = new Thickness(16, 14, 16, 8) };
        heading.Children.Add(new TextBlock
        {
            Text = "ตรวจความพร้อมก่อนเริ่มงาน",
            FontSize = 24,
            FontWeight = FontWeights.Bold
        });
        heading.Children.Add(new TextBlock
        {
            Text = "ตรวจโปรแกรม โมเดล ฟอนต์ GPU พื้นที่ดิสก์ และสิทธิ์เขียน โดยไม่บันทึก API Key",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 5, 0, 0)
        });
        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        var statusPanel = new StackPanel();
        statusPanel.Children.Add(_summary);
        statusPanel.Children.Add(_progress);
        Grid.SetRow(statusPanel, 1);
        root.Children.Add(statusPanel);

        Grid.SetRow(_grid, 2);
        root.Children.Add(_grid);

        var buttons = new DockPanel { Margin = new Thickness(12, 0, 12, 12) };
        var close = new Button { Content = "ปิด", Margin = new Thickness(4), Padding = new Thickness(14, 7, 14, 7) };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        DockPanel.SetDock(_exportButton, Dock.Right);
        DockPanel.SetDock(_runButton, Dock.Right);
        buttons.Children.Add(close);
        buttons.Children.Add(_exportButton);
        buttons.Children.Add(_runButton);
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);
        return root;
    }

    private void ConfigureColumns()
    {
        _grid.Columns.Add(new DataGridTextColumn { Header = "หมวด", Binding = new Binding(nameof(DoctorCheck.Category)), Width = 140 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "รายการ", Binding = new Binding(nameof(DoctorCheck.Name)), Width = 180 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "สถานะ", Binding = new Binding(nameof(DoctorCheck.State)), Width = 100 });
        _grid.Columns.Add(new DataGridCheckBoxColumn { Header = "จำเป็น", Binding = new Binding(nameof(DoctorCheck.Required)), Width = 70 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "ผลตรวจ", Binding = new Binding(nameof(DoctorCheck.Summary)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "รายละเอียด", Binding = new Binding(nameof(DoctorCheck.Details)), Width = new DataGridLength(1.4, DataGridLengthUnitType.Star) });
    }

    private async Task RunDoctorAsync()
    {
        _runButton.IsEnabled = false;
        _exportButton.IsEnabled = false;
        _progress.Visibility = Visibility.Visible;
        _summary.Text = "กำลังตรวจระบบ...";
        _grid.ItemsSource = null;
        try
        {
            _report = await _service.RunAsync(_projectRoot);
            _grid.ItemsSource = _report.Checks;
            _summary.Text = (_report.HasRequiredFailures ? "มีรายการจำเป็นที่ต้องแก้ไข — " : "ระบบหลักพร้อม — ") + _report.Summary;
            _summary.Foreground = _report.HasRequiredFailures ? Brushes.DarkRed : Brushes.DarkGreen;
            _exportButton.IsEnabled = true;
        }
        catch (Exception exception)
        {
            _summary.Text = "ตรวจระบบไม่สำเร็จ: " + exception.Message;
            _summary.Foreground = Brushes.DarkRed;
        }
        finally
        {
            _progress.Visibility = Visibility.Collapsed;
            _runButton.IsEnabled = true;
        }
    }

    private async Task ExportAsync()
    {
        if (_report is null) return;
        var dialog = new SaveFileDialog
        {
            Title = "บันทึกรายงาน System Doctor",
            Filter = "JSON|*.json",
            FileName = $"autocut-system-doctor-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };
        if (dialog.ShowDialog(this) != true) return;

        await SystemDoctorService.ExportAsync(_report, dialog.FileName);
        MessageBox.Show(this, "บันทึกรายงานแล้ว\n" + dialog.FileName, "System Doctor", MessageBoxButton.OK, MessageBoxImage.Information);

        var directory = Path.GetDirectoryName(dialog.FileName);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            Process.Start(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
    }
}
