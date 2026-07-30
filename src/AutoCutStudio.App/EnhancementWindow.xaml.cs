using System.Windows;
using System.Windows.Controls;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Infrastructure;
using Microsoft.Win32;

namespace AutoCutStudio.App;

public partial class EnhancementWindow : Window
{
    private readonly string _sourcePath;
    private readonly double _durationSeconds;
    private readonly ToolLocator _tools;
    private string? _musicPath;

    public EnhancementWindow(string sourcePath, double durationSeconds, ToolLocator tools)
    {
        InitializeComponent();
        _sourcePath = sourcePath;
        _durationSeconds = durationSeconds;
        _tools = tools;
    }

    public EnhancementPlan? Plan { get; private set; }
    public BeatAnalysisResult? BeatAnalysis { get; private set; }

    private void BrowseMusic_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "เลือกเพลงประกอบ",
            Filter = "Audio files|*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg"
        };
        if (dialog.ShowDialog() != true) return;
        _musicPath = dialog.FileName;
        MusicPathTextBox.Text = _musicPath;
    }

    private void ClearMusic_Click(object sender, RoutedEventArgs e)
    {
        _musicPath = null;
        MusicPathTextBox.Text = "ยังไม่ได้เลือกเพลง";
    }

    private async void AnalyzeBeat_Click(object sender, RoutedEventArgs e)
    {
        var target = _musicPath ?? _sourcePath;
        AnalyzeBeatButton.IsEnabled = false;
        BeatStatusText.Text = "กำลัง Decode PCM และตรวจ Energy Peaks...";
        try
        {
            BeatAnalysis = await new FfmpegBeatDetector(_tools).AnalyzeAsync(target, _durationSeconds);
            BeatGrid.ItemsSource = BeatAnalysis.Beats.Take(300).ToList();
            BeatStatusText.Text =
                $"พบ Beat {BeatAnalysis.Beats.Count:N0} จุด | BPM โดยประมาณ {BeatAnalysis.EstimatedBpm:0.0} | วิธี {BeatAnalysis.Method}";
        }
        catch (Exception exception)
        {
            BeatStatusText.Text = $"วิเคราะห์ Beat ไม่สำเร็จ: {exception.Message}";
        }
        finally
        {
            AnalyzeBeatButton.IsEnabled = true;
        }
    }

    private string SelectedTag(ComboBox comboBox) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "none";

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        Plan = new EnhancementPlan
        {
            AudioPreset = SelectedTag(AudioPresetComboBox),
            ColorPreset = SelectedTag(ColorPresetComboBox),
            Stabilize = StabilizeCheckBox.IsChecked == true,
            MusicPath = _musicPath,
            EnableMusicDucking = DuckingCheckBox.IsChecked == true && _musicPath is not null,
            MusicVolume = MusicVolumeSlider.Value
        };
        if (Plan.AudioPreset == AudioEnhancementPresets.None &&
            Plan.ColorPreset == ColorPresets.None &&
            !Plan.Stabilize && Plan.MusicPath is null)
        {
            MessageBox.Show("เลือกอย่างน้อยหนึ่ง Enhancement", "AutoCut Studio");
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
