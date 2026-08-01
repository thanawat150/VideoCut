using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfButton = System.Windows.Controls.Button;
using WpfGroupBox = System.Windows.Controls.GroupBox;

namespace AutoCutStudio.Rebuild;

public sealed partial class MainWindow
{
    private UIElement BuildUi()
    {
        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(220) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddToolbar(root);
        AddWorkspace(root);
        AddTimeline(root);
        AddFooter(root);
        return root;
    }

    private void AddToolbar(Grid root)
    {
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
        toolbar.Children.Add(CreateButton("สร้างโปรเจกต์", NewProject));
        toolbar.Children.Add(CreateButton("เปิดโปรเจกต์", OpenProject));
        toolbar.Children.Add(CreateButton("บันทึก", SaveProject));
        toolbar.Children.Add(CreateButton("นำเข้าวิดีโอ", Import));
        toolbar.Children.Add(CreateButton("ตรวจระบบ", ShowSystemDoctor));
        toolbar.Children.Add(CreateButton("Export MP4", Export));
        toolbar.Children.Add(CreateButton("ยกเลิกงาน", CancelJob));
        Grid.SetRow(toolbar, 0);
        root.Children.Add(toolbar);
    }

    private void AddWorkspace(Grid root)
    {
        var workspace = new Grid { Margin = new Thickness(0, 8, 0, 8) };
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition());
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        workspace.Children.Add(CreateGroup("คลังสื่อ", _media, 0));
        workspace.Children.Add(CreateGroup("Preview", BuildPreview(), 1));
        workspace.Children.Add(CreateGroup("เครื่องมืออัตโนมัติ", BuildAutomation(), 2));
        Grid.SetRow(workspace, 1);
        root.Children.Add(workspace);
    }

    private UIElement BuildPreview()
    {
        var grid = new Grid { Background = Brushes.Black };
        grid.Children.Add(_preview);
        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0))
        };
        controls.Children.Add(CreateButton("▶", (_, _) => _preview.Play()));
        controls.Children.Add(CreateButton("⏸", (_, _) => _preview.Pause()));
        controls.Children.Add(CreateButton("⏹", (_, _) => _preview.Stop()));
        grid.Children.Add(controls);
        return grid;
    }
}
