using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;
using AutoCutStudio.Infrastructure;

namespace AutoCutStudio.App;

public partial class TranscriptWindow : Window
{
    private readonly string _projectRoot;
    private readonly string _cacheRoot;
    private readonly Guid _projectId;
    private readonly MediaAsset _media;
    private readonly ToolLocator _mediaTools;
    private readonly SpeechToolLocator _speechTools;
    private readonly Func<AgentEvent, Task>? _eventSink;
    private readonly Action<double, double>? _previewSegment;
    private readonly TranscriptRepository _repository = new();
    private readonly TranscriptEditingService _editor = new();
    private CancellationTokenSource? _transcriptionCancellation;

    public TranscriptWindow(
        string projectRoot,
        string cacheRoot,
        Guid projectId,
        MediaAsset media,
        ToolLocator mediaTools,
        SpeechToolLocator speechTools,
        Func<AgentEvent, Task>? eventSink,
        Action<double, double>? previewSegment)
    {
        InitializeComponent();
        _projectRoot = projectRoot;
        _cacheRoot = cacheRoot;
        _projectId = projectId;
        _media = media;
        _mediaTools = mediaTools;
        _speechTools = speechTools;
        _eventSink = eventSink;
        _previewSegment = previewSegment;
        Loaded += TranscriptWindow_Loaded;
        Closing += TranscriptWindow_Closing;
    }

    public TranscriptDocument? Transcript { get; private set; }
    public bool ApplyTimelineRequested { get; private set; }

    private async void TranscriptWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshSpeechToolStatus();
        Transcript = await _repository.OpenAsync(_projectRoot);
        if (Transcript is not null && Transcript.MediaAssetId == _media.Id)
        {
            BindTranscript(Transcript);
            SpeechStatusText.Text = "เปิด Transcript เดิมแล้ว สามารถแก้ไข ค้นหา และ Export SRT ได้";
        }
        else
        {
            Transcript = null;
            SpeechStatusText.Text = "ยังไม่มี Transcript สำหรับวิดีโอนี้ กด ‘ถอดเสียงใหม่’";
        }
    }

    private void TranscriptWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _transcriptionCancellation?.Cancel();
        _transcriptionCancellation?.Dispose();
    }

    private void RefreshSpeechToolStatus()
    {
        var availability = _speechTools.Locate();
        SpeechToolStatusText.Text =
            $"{availability.Status}: {availability.Message}\n" +
            $"CLI: {availability.WhisperCliPath ?? "ไม่พบ"}\n" +
            $"Model: {availability.ModelPath ?? "ไม่พบ"}";
        SpeechToolStatusText.Foreground = availability.IsReady
            ? System.Windows.Media.Brushes.LightGreen
            : System.Windows.Media.Brushes.Orange;
        TranscribeButton.IsEnabled = availability.IsReady;
    }

    private async void Transcribe_Click(object sender, RoutedEventArgs e)
    {
        var speechAvailability = _speechTools.Locate();
        if (!speechAvailability.IsReady)
        {
            MessageBox.Show(
                speechAvailability.Message,
                "Whisper Dependency ไม่พร้อม",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!_media.Metadata.HasAudio)
        {
            MessageBox.Show("วิดีโอนี้ไม่มี Audio Stream", "AutoCut Studio");
            return;
        }

        _transcriptionCancellation?.Cancel();
        _transcriptionCancellation?.Dispose();
        _transcriptionCancellation = new CancellationTokenSource();
        SetBusy(true, "กำลังเตรียมถอดเสียงภายในเครื่อง");

        try
        {
            await EmitAsync(new AgentEvent
            {
                EventType = "transcription.started",
                ProjectId = _projectId,
                AgentId = AgentIds.MediaAnalyst,
                Action = "local_speech_to_text",
                Status = AgentStatuses.Analysing,
                Progress = 0,
                Message = "เริ่มถอดเสียงด้วย Whisper Local",
                InputPath = _media.SourcePath
            });

            var service = new WhisperTranscriptionService(_mediaTools, _speechTools);
            var options = new TranscriptionOptions
            {
                Language = SelectedLanguage(),
                Threads = Math.Clamp(Math.Max(2, Environment.ProcessorCount - 1), 2, 16),
                UseGpu = UseGpuCheckBox.IsChecked == true,
                InitialPrompt = SelectedLanguage() == "th" ? "ภาษาไทย" : null
            };
            var result = await service.TranscribeAsync(
                _media.SourcePath,
                _projectRoot,
                _cacheRoot,
                _projectId,
                _media.Id,
                options,
                async (value, message) =>
                {
                    SpeechProgressBar.IsIndeterminate = value is null;
                    if (value is not null)
                    {
                        SpeechProgressBar.Value = Math.Clamp(value.Value, 0, 100);
                    }

                    SpeechStatusText.Text = message;
                    await Task.CompletedTask;
                },
                _transcriptionCancellation.Token);

            Transcript = result.Transcript;
            TechnicalLogTextBox.Text = result.TechnicalLogTail +
                                       Environment.NewLine + Environment.NewLine +
                                       result.SafeWhisperCommand;
            BindTranscript(Transcript);
            await _repository.SaveAsync(_projectRoot, Transcript, _transcriptionCancellation.Token);
            var srtPath = await _repository.ExportSrtAsync(
                _projectRoot,
                Transcript,
                _editor,
                _transcriptionCancellation.Token);

            await EmitAsync(new AgentEvent
            {
                EventType = "transcription.completed",
                ProjectId = _projectId,
                AgentId = AgentIds.MediaAnalyst,
                Action = "local_speech_to_text",
                Status = AgentStatuses.Completed,
                Progress = 100,
                Message = $"ถอดเสียงสำเร็จ {Transcript.Segments.Count} ช่วง ภาษา {Transcript.DetectedLanguage}",
                InputPath = _media.SourcePath,
                OutputPath = srtPath,
                Metadata = new Dictionary<string, string>
                {
                    ["segments"] = Transcript.Segments.Count.ToString(),
                    ["language"] = Transcript.DetectedLanguage,
                    ["model"] = Transcript.ModelName
                }
            });
            SpeechStatusText.Text = "ถอดเสียงสำเร็จ บันทึก Transcript และ SRT แล้ว";
        }
        catch (OperationCanceledException)
        {
            SpeechStatusText.Text = "ยกเลิกการถอดเสียงแล้ว";
        }
        catch (Exception exception)
        {
            SpeechStatusText.Text = $"ถอดเสียงไม่สำเร็จ: {exception.Message}";
            await EmitAsync(new AgentEvent
            {
                EventType = "transcription.failed",
                ProjectId = _projectId,
                AgentId = AgentIds.MediaAnalyst,
                Action = "local_speech_to_text",
                Status = AgentStatuses.Error,
                Message = exception.Message,
                InputPath = _media.SourcePath,
                Severity = "error",
                RequiresUserAction = true
            });
            MessageBox.Show(
                $"{exception.Message}\n\nSource Media ไม่ถูกแก้ไข",
                "ถอดเสียงไม่สำเร็จ",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, SpeechStatusText.Text);
        }
    }

    private string SelectedLanguage() =>
        (LanguageComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "auto";

    private void BindTranscript(TranscriptDocument transcript)
    {
        TranscriptGrid.ItemsSource = transcript.Segments;
        ApplyTimelineButton.IsEnabled = true;
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        if (Transcript is null)
        {
            TranscriptSummaryTextBox.Text = "ยังไม่มี Transcript";
            return;
        }

        var excluded = Transcript.Segments.Count(item => item.IsExcluded);
        var fillers = Transcript.Segments.Count(item => item.IsFiller);
        TranscriptSummaryTextBox.Text =
            $"ภาษาเลือก: {Transcript.Language}\n" +
            $"ภาษาที่ตรวจพบ: {Transcript.DetectedLanguage}\n" +
            $"Model: {Transcript.ModelName}\n" +
            $"จำนวนช่วง: {Transcript.Segments.Count:N0}\n" +
            $"ทำเครื่องหมายตัดออก: {excluded:N0}\n" +
            $"Filler เดี่ยว: {fillers:N0}\n\n" +
            Transcript.PlainText;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (Transcript is null)
        {
            return;
        }

        TranscriptGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        TranscriptGrid.CommitEdit(DataGridEditingUnit.Row, true);
        await _repository.SaveAsync(_projectRoot, Transcript);
        UpdateSummary();
        SpeechStatusText.Text = "บันทึก Transcript แล้ว";
    }

    private async void ExportSrt_Click(object sender, RoutedEventArgs e)
    {
        if (Transcript is null)
        {
            return;
        }

        TranscriptGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        TranscriptGrid.CommitEdit(DataGridEditingUnit.Row, true);
        await _repository.SaveAsync(_projectRoot, Transcript);
        var path = await _repository.ExportSrtAsync(_projectRoot, Transcript, _editor);
        SpeechStatusText.Text = $"Export SRT แล้ว: {path}";
        await EmitAsync(new AgentEvent
        {
            EventType = "subtitle.exported",
            ProjectId = _projectId,
            AgentId = AgentIds.VideoEditor,
            Action = "export_srt",
            Status = AgentStatuses.Completed,
            Progress = 100,
            Message = "สร้าง Subtitle SRT จาก Transcript ที่แก้ไขแล้ว",
            InputPath = _media.SourcePath,
            OutputPath = path
        });
    }

    private void Search_Click(object sender, RoutedEventArgs e)
    {
        if (Transcript is null)
        {
            return;
        }

        var results = _editor.Search(Transcript, SearchTextBox.Text);
        TranscriptGrid.ItemsSource = results;
        SpeechStatusText.Text = $"พบ {results.Count:N0} ช่วงที่ตรงกับ ‘{SearchTextBox.Text.Trim()}’";
    }

    private void ShowAll_Click(object sender, RoutedEventArgs e)
    {
        if (Transcript is null)
        {
            return;
        }

        SearchTextBox.Clear();
        TranscriptGrid.ItemsSource = Transcript.Segments;
        SpeechStatusText.Text = "แสดง Transcript ทั้งหมด";
    }

    private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Search_Click(sender, new RoutedEventArgs());
        }
    }

    private async void MarkFillers_Click(object sender, RoutedEventArgs e)
    {
        if (Transcript is null)
        {
            return;
        }

        var exclude = MessageBox.Show(
            "ให้ทำเครื่องหมาย ‘ตัดออก’ เฉพาะช่วง Transcript ที่มีแต่คำ Filler เดี่ยว เช่น เอ่อ อ่า อืม หรือไม่?\n\nระบบจะไม่ตัด Filler ที่อยู่ปนในประโยค เพราะไม่มี Word Timestamp ที่แม่นพอ",
            "ตรวจ Filler อย่างปลอดภัย",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
        var count = _editor.MarkIsolatedFillers(Transcript, exclude);
        TranscriptGrid.ItemsSource = Transcript.Segments;
        TranscriptGrid.Items.Refresh();
        await _repository.SaveAsync(_projectRoot, Transcript);
        UpdateSummary();
        SpeechStatusText.Text = $"พบ Filler เดี่ยว {count:N0} ช่วง" +
                                (exclude ? " และทำเครื่องหมายตัดออกแล้ว" : string.Empty);
    }

    private void PlaySelected_Click(object sender, RoutedEventArgs e)
    {
        if (TranscriptGrid.SelectedItem is not TranscriptSegment segment)
        {
            MessageBox.Show("เลือกช่วง Transcript ที่ต้องการเล่น", "AutoCut Studio");
            return;
        }

        _previewSegment?.Invoke(segment.StartSeconds, segment.EndSeconds);
    }

    private void TranscriptGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (Transcript is not null)
        {
            Transcript.ModifiedAt = DateTimeOffset.UtcNow;
        }
    }

    private async void ApplyTimeline_Click(object sender, RoutedEventArgs e)
    {
        if (Transcript is null)
        {
            return;
        }

        TranscriptGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        TranscriptGrid.CommitEdit(DataGridEditingUnit.Row, true);
        await _repository.SaveAsync(_projectRoot, Transcript);
        ApplyTimelineRequested = true;
        DialogResult = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void SetBusy(bool busy, string message)
    {
        SpeechProgressBar.IsIndeterminate = busy;
        TranscribeButton.IsEnabled = !busy && _speechTools.Locate().IsReady;
        LanguageComboBox.IsEnabled = !busy;
        UseGpuCheckBox.IsEnabled = !busy;
        SpeechStatusText.Text = message;
    }

    private Task EmitAsync(AgentEvent agentEvent) =>
        _eventSink?.Invoke(agentEvent) ?? Task.CompletedTask;
}
