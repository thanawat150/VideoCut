using System.Windows;
using System.Windows.Controls;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;

namespace AutoCutStudio.App;

public partial class SocialAutomationWindow : Window
{
    private readonly TranscriptDocument _transcript;
    private readonly double _sourceDurationSeconds;
    private readonly Action<double, double>? _previewSegment;
    private readonly HighlightAnalyzer _analyzer = new();

    public SocialAutomationWindow(
        TranscriptDocument transcript,
        double sourceDurationSeconds,
        Action<double, double>? previewSegment)
    {
        InitializeComponent();
        _transcript = transcript;
        _sourceDurationSeconds = sourceDurationSeconds;
        _previewSegment = previewSegment;
        CandidatesGrid.SelectionChanged += CandidatesGrid_SelectionChanged;
        Loaded += (_, _) => Analyze();
    }

    public SocialClipPlan? Plan { get; private set; }

    private void Analyze_Click(object sender, RoutedEventArgs e) => Analyze();

    private void Analyze()
    {
        var candidates = _analyzer.Analyze(
            _transcript,
            _sourceDurationSeconds,
            new HighlightAnalysisOptions
            {
                TargetDurationSeconds = 32,
                MinimumDurationSeconds = 10,
                MaximumDurationSeconds = 58,
                MaximumCandidates = 8,
                ContextPaddingSeconds = 0.3
            }).ToList();
        CandidatesGrid.ItemsSource = candidates;
        CandidatesGrid.SelectedItem = candidates.FirstOrDefault();
        AnalysisSummaryText.Text = candidates.Count == 0
            ? "ยังไม่พบช่วง Transcript ที่ยาวและต่อเนื่องพอสำหรับคลิปสั้น"
            : $"เสนอ Highlight {candidates.Count} ช่วง — เลือกสร้างได้สูงสุด 3 คลิป";
        UpdatePresetSummary();
        CreateButton.IsEnabled = candidates.Count > 0;
    }

    private void CandidatesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CandidatesGrid.SelectedItem is not HighlightCandidate candidate)
        {
            CandidateDetailsTextBox.Text = string.Empty;
            return;
        }

        CandidateDetailsTextBox.Text =
            $"อันดับ: {candidate.Rank}\n" +
            $"คะแนน: {candidate.Score:0.0}/100\n" +
            $"ช่วงเวลา: {candidate.StartSeconds:0.000} – {candidate.EndSeconds:0.000} วินาที\n" +
            $"ความยาว: {candidate.DurationSeconds:0.0} วินาที\n\n" +
            "เหตุผล:\n" + string.Join(Environment.NewLine, candidate.Reasons.Select(reason => "• " + reason)) +
            "\n\nข้อความ:\n" + candidate.PreviewText;
    }

    private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            UpdatePresetSummary();
        }
    }

    private void UpdatePresetSummary()
    {
        var preset = SelectedPreset();
        PresetSummaryText.Text =
            $"{preset.DisplayName}: {preset.Width}×{preset.Height} @ {preset.FrameRate} fps | " +
            (preset.AspectStrategy == "center_crop" ? "Center Crop" : "Fit + Pad");
    }

    private SocialExportPreset SelectedPreset()
    {
        var id = (PresetComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "vertical";
        return id switch
        {
            "youtube" => new SocialExportPreset
            {
                Id = "youtube",
                DisplayName = "YouTube Landscape",
                Width = 1920,
                Height = 1080,
                FrameRate = 30,
                AspectStrategy = "fit_pad",
                VideoBitrateKbps = 10000
            },
            "square" => new SocialExportPreset
            {
                Id = "square",
                DisplayName = "Square Feed",
                Width = 1080,
                Height = 1080,
                FrameRate = 30,
                AspectStrategy = "center_crop",
                VideoBitrateKbps = 7000
            },
            _ => new SocialExportPreset
            {
                Id = "vertical",
                DisplayName = "TikTok / Reels / Shorts",
                Width = 1080,
                Height = 1920,
                FrameRate = 30,
                AspectStrategy = "center_crop",
                VideoBitrateKbps = 8000
            }
        };
    }

    private void PlaySelected_Click(object sender, RoutedEventArgs e)
    {
        if (CandidatesGrid.SelectedItem is HighlightCandidate candidate)
        {
            _previewSegment?.Invoke(candidate.StartSeconds, candidate.EndSeconds);
        }
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        CandidatesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        CandidatesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var selected = (CandidatesGrid.ItemsSource as IEnumerable<HighlightCandidate> ?? [])
            .Where(candidate => candidate.IsSelected)
            .OrderBy(candidate => candidate.Rank)
            .Take(3)
            .ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("เลือกอย่างน้อย 1 Highlight", "AutoCut Studio");
            return;
        }

        Plan = new SocialClipPlan
        {
            ProjectId = _transcript.ProjectId,
            MediaAssetId = _transcript.MediaAssetId,
            Preset = SelectedPreset(),
            Candidates = selected,
            HookText = HookTextBox.Text.Trim(),
            CtaText = CtaTextBox.Text.Trim(),
            BurnCaptions = BurnCaptionsCheckBox.IsChecked == true
        };
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
