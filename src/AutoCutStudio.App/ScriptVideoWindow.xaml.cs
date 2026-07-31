using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;
using AutoCutStudio.Infrastructure;
using Microsoft.Win32;

namespace AutoCutStudio.App;

public partial class ScriptVideoWindow : Window
{
    private readonly ProjectDocument _project;
    private readonly JobRepository _jobs;
    private readonly ScriptVideoPlanner _planner = new();
    private ScriptVideoPlan? _plan;

    public ScriptVideoWindow(ProjectDocument project, JobRepository jobs)
    {
        InitializeComponent();
        _project = project;
        _jobs = jobs;
        var assetsDirectory = Path.Combine(project.RootPath, "assets", "broll");
        Directory.CreateDirectory(assetsDirectory);
        AssetsDirectoryTextBox.Text = assetsDirectory;
        Loaded += (_, _) => PlanScript();
    }

    public JobDocument? CreatedJob { get; private set; }

    private void Plan_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PlanScript();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "วิเคราะห์สคริปต์ไม่สำเร็จ", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PlanScript()
    {
        var options = new ScriptVideoPlanningOptions
        {
            MaximumSceneCharacters = SelectedPlatformId() == "youtube_landscape" ? 320 : 180,
            MinimumSceneDurationSeconds = 3,
            MaximumSceneDurationSeconds = SelectedPlatformId() == "youtube_landscape" ? 20 : 12,
            UseSequentialAssetFallback = SequentialFallbackCheckBox.IsChecked == true
        };
        _plan = _planner.Plan(
            ScriptTextBox.Text,
            AutoVisualCheckBox.IsChecked == true ? AssetsDirectoryTextBox.Text : null,
            options,
            TitleTextBox.Text);
        ScenesGrid.ItemsSource = _plan.Scenes.Select(scene => new SceneRow(scene)).ToList();
        PlanSummaryText.Text = $"{_plan.Scenes.Count} ฉาก · ประมาณ {_plan.EstimatedDurationSeconds:0.0} วินาที · มีภาพประกอบ {_plan.SceneAssets().Count} ฉาก";
        StatusText.Text = _plan.SceneAssets().Count == 0
            ? "ยังไม่พบภาพที่ใช้ได้ ระบบจะสร้าง Motion Text บน Background และเสียงบรรยาย"
            : "ตรวจแผนฉากแล้ว กดสร้างวิดีโอเพื่อส่งเข้า Job Queue";
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetBusy(true, "กำลังเตรียมฉาก คัดลอกภาพ และสร้างเสียงบรรยาย Local...");
            PlanScript();
            var plan = _plan ?? throw new InvalidOperationException("ยังไม่มีแผน Script Video");
            var preset = new PlatformPresetCatalog().Get(SelectedPlatformId());
            var recipe = new TemplateVideoRecipe
            {
                Title = plan.Title,
                Width = preset.Width,
                Height = preset.Height,
                FrameRate = preset.FrameRate,
                ThemeId = "cinematic_visual",
                Sections = plan.ToDocumentSections(),
                GenerateWindowsVoiceover = VoiceoverCheckBox.IsChecked == true,
                VoiceLanguage = DetectVoiceLanguage(ScriptTextBox.Text)
            };
            var voiceOptions = new WindowsVoiceoverOptions
            {
                Language = recipe.VoiceLanguage,
                Rate = (int)Math.Round(VoiceRateSlider.Value),
                Volume = 100
            };
            var assets = AutoVisualCheckBox.IsChecked == true
                ? plan.SceneAssets()
                : new Dictionary<int, string>();

            CreatedJob = await new TemplateVideoJobFactory(_jobs).CreateScriptVideoAsync(
                _project,
                recipe,
                assets,
                voiceOptions,
                PronunciationTextBox.Text);
            StatusText.Text = $"สร้าง Job แล้ว: {CreatedJob.JobId:N}\nOutput: {CreatedJob.OutputPath}";
            DialogResult = true;
        }
        catch (Exception exception)
        {
            SetBusy(false, "สร้าง Script Video ไม่สำเร็จ");
            MessageBox.Show(exception.Message, "สร้าง Script Video ไม่สำเร็จ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void AddAssets_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "เพิ่มภาพหรือวิดีโอเข้าคลัง B-roll ของ Project",
            Filter = "Visual assets|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.mp4;*.mov;*.mkv;*.webm|All files|*.*",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            SetBusy(true, "กำลังคัดลอกภาพ/B-roll เข้าคลังของ Project...");
            var destination = AssetsDirectoryTextBox.Text;
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
            StatusText.Text = $"เพิ่มภาพ/B-roll แล้ว {copied} ไฟล์ และวิเคราะห์ฉากใหม่แล้ว";
            PlanScript();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "เพิ่มภาพไม่สำเร็จ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, StatusText.Text);
        }
    }

    private void OpenAssets_Click(object sender, RoutedEventArgs e)
    {
        var directory = AssetsDirectoryTextBox.Text;
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            ArgumentList = { directory },
            UseShellExecute = true
        });
    }

    private void VoiceRateSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VoiceRateValueText is not null)
            VoiceRateValueText.Text = Math.Round(e.NewValue).ToString("+0;-0;0");
    }

    private string SelectedPlatformId() =>
        (PlatformComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "tiktok";

    private static string DetectVoiceLanguage(string script)
    {
        var thai = script.Count(character => character is >= '\u0E00' and <= '\u0E7F');
        var latin = script.Count(character => char.IsAsciiLetter(character));
        return thai >= latin ? "th" : "en";
    }

    private void SetBusy(bool busy, string message)
    {
        PlanButton.IsEnabled = !busy;
        CreateButton.IsEnabled = !busy;
        BusyProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = message;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (CreatedJob is null)
            DialogResult = false;
        else
            Close();
    }

    private sealed class SceneRow
    {
        public SceneRow(ScriptVideoScene scene)
        {
            DisplayIndex = scene.Index + 1;
            Heading = scene.Heading;
            Narration = scene.Narration;
            AssetDisplayName = scene.AssetDisplayName;
            VisualSourceDisplay = scene.VisualSource switch
            {
                "keyword_match" => "จับคู่คำ",
                "sequence_fallback" => "เรียงลำดับ",
                _ => "Motion Text"
            };
            DurationDisplay = $"{scene.DurationSeconds:0.0}s";
        }

        public int DisplayIndex { get; }
        public string Heading { get; }
        public string Narration { get; }
        public string AssetDisplayName { get; }
        public string VisualSourceDisplay { get; }
        public string DurationDisplay { get; }
    }
}
