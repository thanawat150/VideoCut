using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;
using AutoCutStudio.Infrastructure;
using Microsoft.Win32;

namespace AutoCutStudio.App;

public partial class AutomationStudioWindow : Window
{
    private readonly VisualWorkflowRepository _repository = new();
    private readonly VisualWorkflowExecutor _executor = new();
    private readonly WorkflowTemplateCatalog _templates = new();
    private readonly VisualWorkflowValidator _validator = new();
    private readonly Func<ProjectDocument, IReadOnlyList<JobDocument>, Task> _jobsCreated;
    private readonly Action _openJobCenter;
    private readonly ObservableCollection<ExecutionRow> _executionRows = [];
    private readonly List<NodeDefinition> _nodeDefinitions;
    private readonly Dictionary<Guid, Border> _nodeControls = [];
    private readonly List<(Line Line, VisualWorkflowConnection Connection)> _connectionLines = [];
    private readonly Dictionary<Guid, WorkflowNodeRunState> _runStates = [];

    private ProjectDocument _project;
    private VisualWorkflowDocument _workflow = new();
    private VisualWorkflowNode? _selectedNode;
    private Guid? _connectionSource;
    private Border? _draggingControl;
    private Point _dragStart;
    private Point _dragNodeStart;
    private bool _suppressEditorEvents;
    private bool _suppressWorkflowSelection;
    private CancellationTokenSource? _runCancellation;

    public AutomationStudioWindow(
        ProjectDocument project,
        Func<ProjectDocument, IReadOnlyList<JobDocument>, Task> jobsCreated,
        Action openJobCenter)
    {
        InitializeComponent();
        _project = project;
        _jobsCreated = jobsCreated;
        _openJobCenter = openJobCenter;
        _nodeDefinitions = BuildNodeDefinitions();
        NodeLibraryListBox.ItemsSource = _nodeDefinitions;
        ExecutionGrid.ItemsSource = _executionRows;
        Loaded += AutomationStudioWindow_Loaded;
        Closing += (_, _) => _runCancellation?.Cancel();
    }

    private async void AutomationStudioWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshWorkflowChoicesAsync();
        if (WorkflowComboBox.Items.Count > 0)
        {
            WorkflowComboBox.SelectedIndex = 0;
        }
    }

    private async Task RefreshWorkflowChoicesAsync(Guid? selectWorkflowId = null)
    {
        var choices = new List<WorkflowChoice>();
        choices.AddRange(_templates.CreateDefaults(_project.ProjectId).Select(item => new WorkflowChoice
        {
            DisplayName = $"Template · {item.Name}",
            Workflow = item,
            IsTemplate = true
        }));
        var saved = await _repository.ListAsync(_project.RootPath);
        choices.AddRange(saved.Select(item => new WorkflowChoice
        {
            DisplayName = $"My Flow · {item.Name}",
            Workflow = item,
            IsTemplate = false
        }));

        _suppressWorkflowSelection = true;
        WorkflowComboBox.ItemsSource = choices;
        WorkflowComboBox.DisplayMemberPath = nameof(WorkflowChoice.DisplayName);
        WorkflowComboBox.SelectedItem = selectWorkflowId is null
            ? null
            : choices.FirstOrDefault(item => !item.IsTemplate && item.Workflow.WorkflowId == selectWorkflowId);
        _suppressWorkflowSelection = false;
    }

    private void WorkflowComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressWorkflowSelection || WorkflowComboBox.SelectedItem is not WorkflowChoice choice)
        {
            return;
        }

        _workflow = choice.IsTemplate
            ? CloneAsNewWorkflow(choice.Workflow)
            : DeepClone(choice.Workflow);
        SelectNode(null);
        RenderWorkflow();
        UpdateWorkflowEditor();
        AppendLog(choice.IsTemplate
            ? $"สร้าง Flow ใหม่จาก Template: {choice.Workflow.Name}"
            : $"เปิด Flow: {choice.Workflow.Name}");
    }

    private void WorkflowNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEditorEvents)
        {
            return;
        }
        _workflow.Name = string.IsNullOrWhiteSpace(WorkflowNameTextBox.Text)
            ? "Untitled automation"
            : WorkflowNameTextBox.Text.Trim();
        _workflow.ModifiedAt = DateTimeOffset.UtcNow;
        UpdateFlowInfo();
    }

    private void NodeSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = NodeSearchTextBox.Text.Trim();
        NodeLibraryListBox.ItemsSource = string.IsNullOrWhiteSpace(query)
            ? _nodeDefinitions
            : _nodeDefinitions.Where(item =>
                    item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    item.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    item.NodeType.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
    }

    private void NodeLibraryListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e) => AddSelectedLibraryNode();

    private void AddNode_Click(object sender, RoutedEventArgs e) => AddSelectedLibraryNode();

    private void AddSelectedLibraryNode()
    {
        if (NodeLibraryListBox.SelectedItem is not NodeDefinition definition)
        {
            MessageBox.Show("เลือก Node จากรายการด้านซ้ายก่อน", "Automation Studio");
            return;
        }

        var node = new VisualWorkflowNode
        {
            NodeType = definition.NodeType,
            DisplayName = definition.DisplayName,
            X = Math.Max(60, CanvasScrollViewer.HorizontalOffset / Math.Max(0.1, ZoomSlider.Value) + 180),
            Y = Math.Max(60, CanvasScrollViewer.VerticalOffset / Math.Max(0.1, ZoomSlider.Value) + 160),
            Settings = new Dictionary<string, string>(definition.DefaultSettings, StringComparer.OrdinalIgnoreCase)
        };
        _workflow.Nodes.Add(node);
        _workflow.ModifiedAt = DateTimeOffset.UtcNow;
        RenderWorkflow();
        SelectNode(node);
    }

    private void RemoveNode_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedNode is null)
        {
            return;
        }

        var nodeId = _selectedNode.NodeId;
        _workflow.Nodes.RemoveAll(item => item.NodeId == nodeId);
        _workflow.Connections.RemoveAll(item => item.SourceNodeId == nodeId || item.TargetNodeId == nodeId);
        _connectionSource = _connectionSource == nodeId ? null : _connectionSource;
        SelectNode(null);
        RenderWorkflow();
    }

    private void StartConnect_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedNode is null)
        {
            MessageBox.Show("เลือก Source Node ก่อน", "Automation Studio");
            return;
        }
        _connectionSource = _selectedNode.NodeId;
        CanvasHintText.Text = $"กำลังเชื่อมจาก: {_selectedNode.DisplayName} — เลือกปลายทางแล้วกด ‘เชื่อมมายัง Node นี้’";
        RenderWorkflow();
    }

    private void FinishConnect_Click(object sender, RoutedEventArgs e)
    {
        if (_connectionSource is null || _selectedNode is null)
        {
            MessageBox.Show("กด ‘เริ่มเชื่อมจาก Node นี้’ ที่ Source แล้วเลือก Target", "Automation Studio");
            return;
        }
        if (_connectionSource == _selectedNode.NodeId)
        {
            MessageBox.Show("ไม่สามารถเชื่อม Node กลับเข้าตัวเองได้", "Automation Studio");
            return;
        }
        if (_workflow.Connections.Any(item =>
                item.SourceNodeId == _connectionSource && item.TargetNodeId == _selectedNode.NodeId))
        {
            MessageBox.Show("มีเส้นเชื่อมนี้อยู่แล้ว", "Automation Studio");
            return;
        }

        _workflow.Connections.Add(new VisualWorkflowConnection
        {
            SourceNodeId = _connectionSource.Value,
            TargetNodeId = _selectedNode.NodeId
        });
        _connectionSource = null;
        CanvasHintText.Text = "เชื่อม Node แล้ว";
        RenderWorkflow();
        UpdateSelectedNodeEditor();
    }

    private void RemoveIncomingConnections_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedNode is null)
        {
            return;
        }
        _workflow.Connections.RemoveAll(item => item.TargetNodeId == _selectedNode.NodeId);
        RenderWorkflow();
        UpdateSelectedNodeEditor();
    }

    private async void AddAssets_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "เลือกภาพหรือวิดีโอ B-roll",
            Filter = "B-roll media|*.jpg;*.jpeg;*.png;*.webp;*.mp4;*.mov;*.mkv|All files|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var destination = Path.Combine(_project.RootPath, "assets", "broll");
        Directory.CreateDirectory(destination);
        var copied = 0;
        foreach (var source in dialog.FileNames)
        {
            var extension = Path.GetExtension(source);
            var target = VersionedPathService.GetNextAvailablePath(
                destination,
                VersionedPathService.SanitizeFileName(Path.GetFileNameWithoutExtension(source)),
                extension);
            await using var input = File.OpenRead(source);
            await using var output = File.Create(target);
            await input.CopyToAsync(output);
            copied++;
        }
        AppendLog($"เพิ่ม B-roll เข้า Project แล้ว {copied} ไฟล์");
        MessageBox.Show($"เพิ่มภาพ/B-roll แล้ว {copied} ไฟล์\n\n{destination}", "Automation Studio");
    }

    private void RenderWorkflow()
    {
        WorkflowCanvas.Children.Clear();
        _nodeControls.Clear();
        _connectionLines.Clear();

        foreach (var connection in _workflow.Connections)
        {
            var line = new Line
            {
                Stroke = new SolidColorBrush(Color.FromRgb(81, 116, 255)),
                StrokeThickness = 3,
                Opacity = 0.9,
                IsHitTestVisible = false
            };
            WorkflowCanvas.Children.Add(line);
            _connectionLines.Add((line, connection));
        }

        foreach (var node in _workflow.Nodes)
        {
            var card = CreateNodeCard(node);
            Canvas.SetLeft(card, node.X);
            Canvas.SetTop(card, node.Y);
            WorkflowCanvas.Children.Add(card);
            _nodeControls[node.NodeId] = card;
        }

        UpdateConnectionLines();
        UpdateFlowInfo();
    }

    private Border CreateNodeCard(VisualWorkflowNode node)
    {
        var status = _runStates.GetValueOrDefault(node.NodeId)?.Status;
        var selected = _selectedNode?.NodeId == node.NodeId;
        var connectionSource = _connectionSource == node.NodeId;
        var borderBrush = connectionSource
            ? Brushes.Gold
            : selected
                ? new SolidColorBrush(Color.FromRgb(70, 214, 190))
                : StatusBrush(status);
        var card = new Border
        {
            Width = 200,
            Height = 96,
            CornerRadius = new CornerRadius(13),
            Background = node.IsDisabled
                ? new SolidColorBrush(Color.FromRgb(30, 36, 49))
                : new SolidColorBrush(Color.FromRgb(20, 31, 52)),
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(selected || connectionSource ? 2.5 : 1.5),
            Padding = new Thickness(11),
            Tag = node.NodeId,
            Cursor = Cursors.Hand,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 14,
                ShadowDepth = 2,
                Opacity = 0.28,
                Color = Colors.Black
            }
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var icon = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(10),
            Background = NodeAccentBrush(node.NodeType),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = NodeGlyph(node.NodeType),
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetRow(icon, 0);
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var titlePanel = new StackPanel { Margin = new Thickness(7, 0, 0, 0) };
        titlePanel.Children.Add(new TextBlock
        {
            Text = node.DisplayName,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13.5,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = node.NodeType,
            Foreground = new SolidColorBrush(Color.FromRgb(143, 158, 184)),
            FontSize = 10.5,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetRow(titlePanel, 0);
        Grid.SetColumn(titlePanel, 1);
        grid.Children.Add(titlePanel);

        var statusText = new TextBlock
        {
            Text = node.IsDisabled ? "disabled" : status ?? "ready",
            Foreground = node.IsDisabled ? Brushes.Gray : borderBrush,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 7, 0, 0)
        };
        Grid.SetRow(statusText, 1);
        Grid.SetColumnSpan(statusText, 2);
        grid.Children.Add(statusText);

        var inputDot = new Ellipse
        {
            Width = 12,
            Height = 12,
            Fill = new SolidColorBrush(Color.FromRgb(67, 85, 119)),
            Stroke = Brushes.White,
            StrokeThickness = 1,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(inputDot, -6);
        Canvas.SetTop(inputDot, 42);
        var outputDot = new Ellipse
        {
            Width = 12,
            Height = 12,
            Fill = new SolidColorBrush(Color.FromRgb(74, 222, 184)),
            Stroke = Brushes.White,
            StrokeThickness = 1,
            IsHitTestVisible = false
        };
        Canvas.SetRight(outputDot, -6);
        Canvas.SetTop(outputDot, 42);

        var host = new Grid();
        host.Children.Add(grid);
        host.Children.Add(inputDot);
        host.Children.Add(outputDot);
        card.Child = host;
        card.MouseLeftButtonDown += NodeCard_MouseLeftButtonDown;
        card.MouseMove += NodeCard_MouseMove;
        card.MouseLeftButtonUp += NodeCard_MouseLeftButtonUp;
        return card;
    }

    private void NodeCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border card || card.Tag is not Guid nodeId)
        {
            return;
        }
        var node = _workflow.Nodes.First(item => item.NodeId == nodeId);
        SelectNode(node);
        _draggingControl = card;
        _dragStart = e.GetPosition(WorkflowCanvas);
        _dragNodeStart = new Point(node.X, node.Y);
        card.CaptureMouse();
        e.Handled = true;
    }

    private void NodeCard_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingControl is null || e.LeftButton != MouseButtonState.Pressed || _selectedNode is null)
        {
            return;
        }
        var current = e.GetPosition(WorkflowCanvas);
        _selectedNode.X = Math.Clamp(_dragNodeStart.X + current.X - _dragStart.X, 10, WorkflowCanvas.Width - 220);
        _selectedNode.Y = Math.Clamp(_dragNodeStart.Y + current.Y - _dragStart.Y, 10, WorkflowCanvas.Height - 120);
        Canvas.SetLeft(_draggingControl, _selectedNode.X);
        Canvas.SetTop(_draggingControl, _selectedNode.Y);
        UpdateConnectionLines();
    }

    private void NodeCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggingControl is not null)
        {
            _draggingControl.ReleaseMouseCapture();
            _draggingControl = null;
            _workflow.ModifiedAt = DateTimeOffset.UtcNow;
        }
        e.Handled = true;
    }

    private void UpdateConnectionLines()
    {
        foreach (var item in _connectionLines)
        {
            if (!_nodeControls.TryGetValue(item.Connection.SourceNodeId, out var source) ||
                !_nodeControls.TryGetValue(item.Connection.TargetNodeId, out var target))
            {
                item.Line.Visibility = Visibility.Collapsed;
                continue;
            }
            item.Line.Visibility = Visibility.Visible;
            item.Line.X1 = Canvas.GetLeft(source) + source.Width;
            item.Line.Y1 = Canvas.GetTop(source) + source.Height / 2;
            item.Line.X2 = Canvas.GetLeft(target);
            item.Line.Y2 = Canvas.GetTop(target) + target.Height / 2;
        }
    }

    private void WorkflowCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == WorkflowCanvas)
        {
            SelectNode(null);
        }
    }

    private void SelectNode(VisualWorkflowNode? node)
    {
        _selectedNode = node;
        UpdateSelectedNodeEditor();
        if (IsLoaded)
        {
            RenderWorkflow();
        }
    }

    private void UpdateWorkflowEditor()
    {
        _suppressEditorEvents = true;
        WorkflowNameTextBox.Text = _workflow.Name;
        _suppressEditorEvents = false;
        UpdateSelectedNodeEditor();
        UpdateFlowInfo();
    }

    private void UpdateSelectedNodeEditor()
    {
        _suppressEditorEvents = true;
        NoNodeSelectedPanel.Visibility = _selectedNode is null ? Visibility.Visible : Visibility.Collapsed;
        NodeSettingsPanel.Visibility = _selectedNode is null ? Visibility.Collapsed : Visibility.Visible;
        if (_selectedNode is not null)
        {
            SelectedNodeNameTextBox.Text = _selectedNode.DisplayName;
            SelectedNodeTypeText.Text = _selectedNode.NodeType;
            SelectedNodeDisabledCheckBox.IsChecked = _selectedNode.IsDisabled;
            NodeSettingsTextBox.Text = string.Join(
                Environment.NewLine,
                _selectedNode.Settings.OrderBy(item => item.Key).Select(item => $"{item.Key}={item.Value}"));
            var incoming = _workflow.Connections.Count(item => item.TargetNodeId == _selectedNode.NodeId);
            var outgoing = _workflow.Connections.Count(item => item.SourceNodeId == _selectedNode.NodeId);
            NodeConnectionSummaryText.Text = $"Input connections: {incoming}\nOutput connections: {outgoing}";
        }
        _suppressEditorEvents = false;
    }

    private void SelectedNodeNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEditorEvents || _selectedNode is null)
        {
            return;
        }
        var index = _workflow.Nodes.FindIndex(item => item.NodeId == _selectedNode.NodeId);
        var replacement = _selectedNode with
        {
            DisplayName = string.IsNullOrWhiteSpace(SelectedNodeNameTextBox.Text)
                ? _selectedNode.NodeType
                : SelectedNodeNameTextBox.Text.Trim()
        };
        _workflow.Nodes[index] = replacement;
        _selectedNode = replacement;
        RenderWorkflow();
    }

    private void SelectedNodeDisabledCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEditorEvents || _selectedNode is null)
        {
            return;
        }
        _selectedNode.IsDisabled = SelectedNodeDisabledCheckBox.IsChecked == true;
        RenderWorkflow();
    }

    private void NodeSettingsTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEditorEvents || _selectedNode is null)
        {
            return;
        }
        var parsed = ParseSettings(NodeSettingsTextBox.Text);
        _selectedNode.Settings.Clear();
        foreach (var item in parsed)
        {
            _selectedNode.Settings[item.Key] = item.Value;
        }
        _workflow.ModifiedAt = DateTimeOffset.UtcNow;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _workflow.Name = string.IsNullOrWhiteSpace(WorkflowNameTextBox.Text)
                ? "Untitled automation"
                : WorkflowNameTextBox.Text.Trim();
            await _repository.SaveAsync(_project.RootPath, _workflow);
            await RefreshWorkflowChoicesAsync(_workflow.WorkflowId);
            AppendLog($"บันทึก Flow: {_workflow.Name}");
            RunSummaryText.Text = "บันทึก Workflow แล้ว";
        }
        catch (Exception exception)
        {
            ShowError("บันทึก Workflow ไม่สำเร็จ", exception);
        }
    }

    private void Validate_Click(object sender, RoutedEventArgs e)
    {
        var validation = _validator.Validate(_workflow);
        var builder = new StringBuilder();
        builder.AppendLine(validation.IsValid ? "✓ Flow ใช้งานได้" : "✕ Flow ยังใช้งานไม่ได้");
        foreach (var error in validation.Errors)
        {
            builder.AppendLine("ERROR: " + error);
        }
        foreach (var warning in validation.Warnings)
        {
            builder.AppendLine("WARNING: " + warning);
        }
        builder.AppendLine($"ลำดับทำงาน: {validation.TopologicalOrder.Count} Node");
        AppendLog(builder.ToString().TrimEnd());
        MessageBox.Show(
            builder.ToString(),
            "ตรวจ Visual Workflow",
            MessageBoxButton.OK,
            validation.IsValid ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (_runCancellation is not null)
        {
            return;
        }
        var validation = _validator.Validate(_workflow);
        if (!validation.IsValid)
        {
            MessageBox.Show(string.Join(Environment.NewLine, validation.Errors), "Flow ยังไม่พร้อม", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            await _repository.SaveAsync(_project.RootPath, _workflow);
            _runCancellation = new CancellationTokenSource();
            CancelRunButton.IsEnabled = true;
            _runStates.Clear();
            _executionRows.Clear();
            foreach (var nodeId in validation.TopologicalOrder)
            {
                var node = _workflow.Nodes.First(item => item.NodeId == nodeId);
                _executionRows.Add(new ExecutionRow(nodeId, node.DisplayName, WorkflowRunStatuses.Queued, 0, "รอทำงาน"));
            }
            RunSummaryText.Text = "กำลัง Run Automation...";
            AppendLog($"RUN {_workflow.Name} | {_workflow.Nodes.Count} nodes");
            RenderWorkflow();

            var result = await _executor.ExecuteAsync(
                _project,
                _workflow,
                selectedMedia: _project.SourceMedia,
                nodeUpdated: async state =>
                {
                    await Dispatcher.InvokeAsync(() => UpdateExecutionState(state));
                },
                requestApproval: async request => await Dispatcher.InvokeAsync(() => RequestApproval(request)),
                cancellationToken: _runCancellation.Token);

            _project = result.UpdatedProject;
            await _jobsCreated(_project, result.CreatedJobs);
            RunSummaryText.Text = result.Run.Status == WorkflowRunStatuses.Completed
                ? $"สำเร็จ — สร้าง {result.CreatedJobs.Count} Job"
                : $"เสร็จพร้อมคำเตือน — สร้าง {result.CreatedJobs.Count} Job";
            AppendLog($"DONE {result.Run.Status} | jobs={result.CreatedJobs.Count} | broll={result.BrollSuggestions.Count}");
            if (result.Run.Warnings.Count > 0)
            {
                AppendLog(string.Join(Environment.NewLine, result.Run.Warnings.Select(item => "WARNING: " + item)));
            }
            RenderWorkflow();
        }
        catch (OperationCanceledException)
        {
            RunSummaryText.Text = "ยกเลิก Automation แล้ว";
            AppendLog("CANCELLED");
        }
        catch (Exception exception)
        {
            RunSummaryText.Text = "Automation ล้มเหลว";
            AppendLog("FAILED: " + exception.Message);
            ShowError("Run Automation ไม่สำเร็จ", exception);
        }
        finally
        {
            _runCancellation?.Dispose();
            _runCancellation = null;
            CancelRunButton.IsEnabled = false;
        }
    }

    private bool RequestApproval(WorkflowApprovalRequest request)
    {
        var preview = string.Join(Environment.NewLine, request.Items.Take(15).Select(item => "• " + item));
        if (request.Items.Count > 15)
        {
            preview += $"\n…และอีก {request.Items.Count - 15} รายการ";
        }
        return MessageBox.Show(
                   $"{request.Message}\n\n{preview}\n\nอนุมัติให้นำภาพเหล่านี้ไปใส่ในวิดีโอหรือไม่?",
                   request.Title,
                   MessageBoxButton.YesNo,
                   MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    private void UpdateExecutionState(WorkflowNodeRunState state)
    {
        _runStates[state.NodeId] = state;
        var row = _executionRows.FirstOrDefault(item => item.NodeId == state.NodeId);
        if (row is not null)
        {
            row.Status = state.Status;
            row.Progress = state.Progress;
            row.Message = state.Message;
            ExecutionGrid.Items.Refresh();
        }
        AppendLog($"{DateTime.Now:HH:mm:ss} | {state.NodeType} | {state.Status} | {state.Progress:0}% | {state.Message}");
        RenderWorkflow();
    }

    private void CancelRun_Click(object sender, RoutedEventArgs e) => _runCancellation?.Cancel();

    private void OpenJobCenter_Click(object sender, RoutedEventArgs e)
    {
        _openJobCenter();
        Close();
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (CanvasViewbox is not null)
        {
            CanvasViewbox.LayoutTransform = new ScaleTransform(e.NewValue, e.NewValue);
        }
    }

    private void UpdateFlowInfo()
    {
        var validation = _validator.Validate(_workflow);
        FlowInfoText.Text =
            $"ชื่อ: {_workflow.Name}\n" +
            $"Nodes: {_workflow.Nodes.Count}\n" +
            $"Connections: {_workflow.Connections.Count}\n" +
            $"สถานะ: {(validation.IsValid ? "พร้อม Run" : $"มีข้อผิดพลาด {validation.Errors.Count} จุด")}\n" +
            $"คำเตือน: {validation.Warnings.Count}";
    }

    private void AppendLog(string message)
    {
        var lines = (ExecutionLogTextBox.Text + message + Environment.NewLine)
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .TakeLast(500);
        ExecutionLogTextBox.Text = string.Join(Environment.NewLine, lines) + Environment.NewLine;
        ExecutionLogTextBox.ScrollToEnd();
    }

    private static Dictionary<string, string> ParseSettings(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }
            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                result[key] = value;
            }
        }
        return result;
    }

    private static VisualWorkflowDocument CloneAsNewWorkflow(VisualWorkflowDocument source)
    {
        var map = source.Nodes.ToDictionary(item => item.NodeId, _ => Guid.NewGuid());
        return new VisualWorkflowDocument
        {
            WorkflowId = Guid.NewGuid(),
            ProjectId = source.ProjectId,
            Name = source.Name,
            Description = source.Description,
            Nodes = source.Nodes.Select(item => new VisualWorkflowNode
            {
                NodeId = map[item.NodeId],
                NodeType = item.NodeType,
                DisplayName = item.DisplayName,
                X = item.X,
                Y = item.Y,
                IsDisabled = item.IsDisabled,
                Settings = new Dictionary<string, string>(item.Settings, StringComparer.OrdinalIgnoreCase)
            }).ToList(),
            Connections = source.Connections.Select(item => new VisualWorkflowConnection
            {
                ConnectionId = Guid.NewGuid(),
                SourceNodeId = map[item.SourceNodeId],
                SourcePort = item.SourcePort,
                TargetNodeId = map[item.TargetNodeId],
                TargetPort = item.TargetPort
            }).ToList()
        };
    }

    private static VisualWorkflowDocument DeepClone(VisualWorkflowDocument source)
    {
        var json = JsonSerializer.Serialize(source, JsonDefaults.Options);
        return JsonSerializer.Deserialize<VisualWorkflowDocument>(json, JsonDefaults.Options)
               ?? throw new InvalidDataException("Clone Workflow ไม่สำเร็จ");
    }

    private static Brush StatusBrush(string? status) => status switch
    {
        WorkflowRunStatuses.Running => new SolidColorBrush(Color.FromRgb(49, 177, 255)),
        WorkflowRunStatuses.WaitingForApproval => Brushes.Gold,
        WorkflowRunStatuses.Completed => new SolidColorBrush(Color.FromRgb(45, 212, 154)),
        WorkflowRunStatuses.CompletedWithWarnings => Brushes.Orange,
        WorkflowRunStatuses.Failed => new SolidColorBrush(Color.FromRgb(248, 93, 111)),
        WorkflowRunStatuses.Cancelled => Brushes.Gray,
        _ => new SolidColorBrush(Color.FromRgb(55, 72, 105))
    };

    private static Brush NodeAccentBrush(string nodeType) => nodeType switch
    {
        VisualNodeTypes.MediaInput or VisualNodeTypes.FolderInput => new SolidColorBrush(Color.FromRgb(42, 132, 255)),
        VisualNodeTypes.Transcribe or VisualNodeTypes.AnalyzeHighlights or VisualNodeTypes.AutoBroll =>
            new SolidColorBrush(Color.FromRgb(144, 92, 246)),
        VisualNodeTypes.RemoveFillers or VisualNodeTypes.RemoveSilence or VisualNodeTypes.MergeClips =>
            new SolidColorBrush(Color.FromRgb(246, 141, 58)),
        VisualNodeTypes.CreateCaptions or VisualNodeTypes.CreateShorts =>
            new SolidColorBrush(Color.FromRgb(230, 74, 166)),
        VisualNodeTypes.PlatformStyle or VisualNodeTypes.ExportVideo =>
            new SolidColorBrush(Color.FromRgb(25, 189, 150)),
        _ => new SolidColorBrush(Color.FromRgb(68, 91, 133))
    };

    private static string NodeGlyph(string type) => type switch
    {
        VisualNodeTypes.MediaInput => "▶",
        VisualNodeTypes.FolderInput => "▣",
        VisualNodeTypes.MergeClips => "⧉",
        VisualNodeTypes.Transcribe => "AI",
        VisualNodeTypes.RemoveFillers => "✂",
        VisualNodeTypes.RemoveSilence => "∿",
        VisualNodeTypes.CreateCaptions => "CC",
        VisualNodeTypes.AnalyzeHighlights => "★",
        VisualNodeTypes.AutoBroll => "▧",
        VisualNodeTypes.VisualApproval => "✓",
        VisualNodeTypes.EnhanceAudio => "♫",
        VisualNodeTypes.Stabilize => "◫",
        VisualNodeTypes.ColorCorrection => "◉",
        VisualNodeTypes.PlatformStyle => "#",
        VisualNodeTypes.CreateShorts => "9:16",
        VisualNodeTypes.ExportVideo => "⇩",
        VisualNodeTypes.Notify => "!",
        _ => "◆"
    };

    private static List<NodeDefinition> BuildNodeDefinitions() =>
    [
        new("INPUT", VisualNodeTypes.MediaInput, "เลือกคลิป", new()),
        new("INPUT", VisualNodeTypes.FolderInput, "เลือกหลายคลิป/โฟลเดอร์", new() { ["sort"] = "captured_time" }),
        new("EDIT", VisualNodeTypes.MergeClips, "รวมหลายคลิป", new() { ["transition"] = "crossfade", ["normalize_audio"] = "true" }),
        new("AI", VisualNodeTypes.Transcribe, "ถอดเสียง Whisper", new() { ["language"] = "th", ["gpu"] = "true" }),
        new("EDIT", VisualNodeTypes.RemoveFillers, "ลบคำฟิลเลอร์", new() { ["mode"] = "isolated_only" }),
        new("EDIT", VisualNodeTypes.RemoveSilence, "ตัดช่วงเงียบ", new() { ["preset"] = "balanced" }),
        new("TEXT", VisualNodeTypes.CreateCaptions, "สร้าง Subtitle", new() { ["style"] = "animated", ["safe_zone"] = "platform" }),
        new("AI", VisualNodeTypes.AnalyzeHighlights, "หา Highlight", new() { ["count"] = "8", ["duration_seconds"] = "45" }),
        new("VISUAL", VisualNodeTypes.AutoBroll, "ใส่ภาพ/B-roll อัตโนมัติ", new() { ["source"] = "project_assets", ["maximum"] = "20" }),
        new("CONTROL", VisualNodeTypes.VisualApproval, "ตรวจภาพประกอบ", new()),
        new("AUDIO", VisualNodeTypes.EnhanceAudio, "ปรับเสียงพูด", new() { ["noise_reduction"] = "true", ["voice_enhancement"] = "true" }),
        new("VIDEO", VisualNodeTypes.Stabilize, "ลดการสั่น", new() { ["mode"] = "basic" }),
        new("VIDEO", VisualNodeTypes.ColorCorrection, "ปรับสี", new() { ["preset"] = "natural" }),
        new("PLATFORM", VisualNodeTypes.PlatformStyle, "Platform Style", new() { ["preset"] = "tiktok" }),
        new("EDIT", VisualNodeTypes.CreateShorts, "สร้าง Shorts", new() { ["count"] = "3" }),
        new("OUTPUT", VisualNodeTypes.ExportVideo, "Export Video", new() { ["preset"] = "tiktok", ["hook"] = "", ["cta"] = "" }),
        new("OUTPUT", VisualNodeTypes.Notify, "แจ้งเตือนเมื่อเสร็จ", new())
    ];

    private static void ShowError(string title, Exception exception) => MessageBox.Show(
        exception.Message,
        title,
        MessageBoxButton.OK,
        MessageBoxImage.Error);

    private sealed record NodeDefinition(
        string Category,
        string NodeType,
        string DisplayName,
        Dictionary<string, string> DefaultSettings)
    {
        public override string ToString() => $"{NodeGlyph(NodeType)}  {DisplayName}\n    {Category}";
    }

    private sealed record WorkflowChoice
    {
        public string DisplayName { get; init; } = string.Empty;
        public VisualWorkflowDocument Workflow { get; init; } = new();
        public bool IsTemplate { get; init; }
    }

    private sealed class ExecutionRow
    {
        public ExecutionRow(Guid nodeId, string displayName, string status, double progress, string message)
        {
            NodeId = nodeId;
            DisplayName = displayName;
            Status = status;
            Progress = progress;
            Message = message;
        }

        public Guid NodeId { get; }
        public string DisplayName { get; }
        public string Status { get; set; }
        public double Progress { get; set; }
        public string ProgressText => $"{Progress:0}%";
        public string Message { get; set; }
    }
}
