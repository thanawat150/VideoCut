using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
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
    private readonly ScenePromptBuilder _promptBuilder = new();
    private readonly LanguageDetectionService _languages = new();
    private readonly ObservableCollection<SceneRow> _sceneRows = [];
    private ScriptVideoPlan? _plan;
    private string? _planFingerprint;
    private CancellationTokenSource? _operationCancellation;

    public ScriptVideoWindow(ProjectDocument project, JobRepository jobs)
    {
        InitializeComponent();
        _project = project;
        _jobs = jobs;
        var assetsDirectory = Path.Combine(project.RootPath, "assets", "broll");
        Directory.CreateDirectory(assetsDirectory);
        AssetsDirectoryTextBox.Text = assetsDirectory;
        ScenesGrid.ItemsSource = _sceneRows;
        Loaded += ScriptVideoWindow_Loaded;
        Closing += (_, _) => _operationCancellation?.Cancel();
    }

    public JobDocument? CreatedJob { get; private set; }

    private async void ScriptVideoWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await RefreshProviderStateAsync();
            PlanScript();
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
        }
    }

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
        var preset = new PlatformPresetCatalog().Get(SelectedPlatformId());
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
        _planFingerprint = CurrentPlanFingerprint();
        var assets = _plan.SceneAssets();
        _sceneRows.Clear();
        foreach (var scene in _plan.Scenes)
        {
            assets.TryGetValue(scene.Index, out var path);
            _sceneRows.Add(new SceneRow(
                scene,
                _promptBuilder.Build(scene.Heading, scene.Narration, preset.Width, preset.Height),
                path));
        }
        UpdatePlanSummary();
        StatusText.Text = assets.Count == 0
            ? "ยังไม่พบภาพใน Project เลือกโหมด AI แล้วกด ‘เจนภาพ/วิดีโอ’ หรือใช้ Motion Text"
            : "ตรวจแผนฉากแล้ว สามารถแก้ Prompt รายฉากก่อน Generate";
    }

    private void EnsureCurrentPlan()
    {
        if (_plan is null || !string.Equals(_planFingerprint, CurrentPlanFingerprint(), StringComparison.Ordinal))
            PlanScript();
    }

    private async void GenerateVisuals_Click(object sender, RoutedEventArgs e)
    {
        if (_operationCancellation is not null)
            return;
        _operationCancellation = new CancellationTokenSource();
        try
        {
            SetBusy(true, "กำลังเตรียม AI Visuals...");
            EnsureCurrentPlan();
            await GenerateVisualsCoreAsync(confirmCost: true, _operationCancellation.Token);
            StatusText.Text = "สร้างภาพ/วิดีโอเสร็จแล้ว ตรวจรายการแต่ละฉากก่อนสร้างวิดีโอรวม";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "ยกเลิกการสร้าง AI Visuals แล้ว";
        }
        catch (Exception exception)
        {
            StatusText.Text = "สร้าง AI Visuals ไม่สำเร็จ";
            MessageBox.Show(exception.Message, "สร้างภาพ/วิดีโอไม่สำเร็จ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _operationCancellation.Dispose();
            _operationCancellation = null;
            SetBusy(false, StatusText.Text);
        }
    }

    private async Task GenerateVisualsCoreAsync(bool confirmCost, CancellationToken cancellationToken)
    {
        var mode = SelectedVisualMode();
        if (mode == "local")
        {
            StatusText.Text = "โหมดนี้ใช้เฉพาะภาพ/B-roll ใน Project จึงไม่มีคำขอ AI";
            return;
        }

        var candidates = _sceneRows.Where(row => mode != "image_missing" || string.IsNullOrWhiteSpace(row.AssetPath)).ToList();
        if (candidates.Count == 0)
        {
            StatusText.Text = "ทุกฉากมีภาพประกอบแล้ว ไม่มีฉากที่ต้อง Generate";
            return;
        }

        if (confirmCost)
        {
            var mediaKind = mode.StartsWith("video", StringComparison.Ordinal) ? "วิดีโอ" : "ภาพ";
            var answer = MessageBox.Show(
                $"กำลังจะส่ง Prompt {candidates.Count} ฉากไปสร้าง{mediaKind}ผ่าน OpenAI API ซึ่งอาจมีค่าใช้จ่ายตามบัญชี ดำเนินการต่อหรือไม่?",
                "ยืนยันการใช้ Cloud Generation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
            {
                StatusText.Text = "ยังไม่ได้ส่งคำขอ Generate";
                return;
            }
        }

        var apiKey = GetOpenAiApiKey();
        var imageProvider = new OpenAiImageGenerationProvider(apiKey);
        var videoProvider = new OpenAiVideoGenerationProvider(apiKey);
        var requiredAvailability = mode.StartsWith("video", StringComparison.Ordinal)
            ? await videoProvider.GetAvailabilityAsync(cancellationToken)
            : await imageProvider.GetAvailabilityAsync(cancellationToken);
        if (!requiredAvailability.IsReady)
            throw new InvalidOperationException(requiredAvailability.Message);

        var preset = new PlatformPresetCatalog().Get(SelectedPlatformId());
        var outputDirectory = Path.Combine(
            _project.RootPath,
            "assets",
            "generated",
            "script-video",
            DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + SanitizeName(TitleTextBox.Text));
        Directory.CreateDirectory(outputDirectory);

        for (var index = 0; index < candidates.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = candidates[index];
            row.Status = $"กำลังสร้าง {index + 1}/{candidates.Count}";
            StatusText.Text = $"กำลังสร้างฉาก {row.DisplayIndex}/{_sceneRows.Count}: {row.Heading}";

            if (mode is "video_all" or "video_fallback")
            {
                try
                {
                    var output = VersionedPathService.GetNextAvailablePath(
                        outputDirectory,
                        $"scene-{row.SceneIndex:000}-video",
                        ".mp4");
                    var result = await videoProvider.GenerateAsync(new VideoGenerationRequest
                    {
                        Prompt = row.Prompt,
                        OutputPath = output,
                        ReferenceImagePath = IsImage(row.AssetPath) ? row.AssetPath : null,
                        Width = preset.Width,
                        Height = preset.Height,
                        DurationSeconds = SelectedVideoSeconds(),
                        Model = "sora-2"
                    }, cancellationToken);
                    row.SetAsset(result.Path, "OpenAI Video", "สร้างวิดีโอแล้ว");
                    continue;
                }
                catch (Exception exception) when (mode == "video_fallback" && exception is not OperationCanceledException)
                {
                    row.Status = "วิดีโอล้มเหลว กำลังใช้ภาพ AI แทน";
                }
            }

            var imageOutput = VersionedPathService.GetNextAvailablePath(
                outputDirectory,
                $"scene-{row.SceneIndex:000}-image",
                ".png");
            var image = await imageProvider.GenerateAsync(new ImageGenerationRequest
            {
                Prompt = row.Prompt,
                OutputPath = imageOutput,
                Width = preset.Width,
                Height = preset.Height,
                Quality = SelectedImageQuality(),
                Model = "gpt-image-1"
            }, cancellationToken);
            row.SetAsset(image.Path, "OpenAI Image", "สร้างภาพแล้ว");
        }
        UpdatePlanSummary();
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        if (_operationCancellation is not null)
            return;
        _operationCancellation = new CancellationTokenSource();
        try
        {
            SetBusy(true, "กำลังเตรียมฉาก เสียงหลายภาษา และภาพประกอบ...");
            EnsureCurrentPlan();
            if (SelectedVisualMode() != "local" && RowsRequireGeneration())
                await GenerateVisualsCoreAsync(confirmCost: true, _operationCancellation.Token);

            var plan = _plan ?? throw new InvalidOperationException("ยังไม่มีแผน Script Video");
            var preset = new PlatformPresetCatalog().Get(SelectedPlatformId());
            var language = SelectedLanguage();
            var recipe = new TemplateVideoRecipe
            {
                Title = plan.Title,
                Width = preset.Width,
                Height = preset.Height,
                FrameRate = preset.FrameRate,
                ThemeId = "cinematic_visual",
                Sections = plan.ToDocumentSections(),
                GenerateWindowsVoiceover = false,
                VoiceLanguage = language
            };

            var speechProvider = CreateSpeechProvider();
            SpeechSynthesisRequest? speechRequest = null;
            if (speechProvider is not null)
            {
                var availability = await speechProvider.GetAvailabilityAsync(_operationCancellation.Token);
                if (!availability.IsReady)
                    throw new InvalidOperationException(availability.Message);
                speechRequest = new SpeechSynthesisRequest
                {
                    Language = language,
                    Voice = SelectedVoice(),
                    Speed = VoiceSpeedSlider.Value,
                    Rate = (int)Math.Round((VoiceSpeedSlider.Value - 1.0) * 6.0),
                    Volume = 100,
                    Model = "gpt-4o-mini-tts",
                    Instructions = $"Natural, clear narration in {_languages.DisplayName(language)}. Use a warm professional tone and preserve the supplied pronunciation."
                };
            }

            var assets = _sceneRows
                .Where(row => !string.IsNullOrWhiteSpace(row.AssetPath) && File.Exists(row.AssetPath))
                .ToDictionary(row => row.SceneIndex, row => row.AssetPath!);
            CreatedJob = await new TemplateVideoJobFactory(_jobs).CreateScriptVideoWithProviderAsync(
                _project,
                recipe,
                assets,
                speechProvider,
                speechRequest,
                PronunciationTextBox.Text,
                _operationCancellation.Token);
            StatusText.Text = $"สร้าง Job แล้ว: {CreatedJob.JobId:N}\nOutput: {CreatedJob.OutputPath}";
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "ยกเลิกการสร้าง Script Video แล้ว";
        }
        catch (Exception exception)
        {
            StatusText.Text = "สร้าง Script Video ไม่สำเร็จ";
            MessageBox.Show(exception.Message, "สร้าง Script Video ไม่สำเร็จ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _operationCancellation?.Dispose();
            _operationCancellation = null;
            if (IsVisible)
                SetBusy(false, StatusText.Text);
        }
    }

    private ISpeechSynthesisProvider? CreateSpeechProvider()
    {
        if (VoiceoverCheckBox.IsChecked != true)
            return null;
        return SelectedTag(VoiceProviderComboBox, "windows") switch
        {
            "none" => null,
            "openai" => new OpenAiSpeechSynthesisProvider(GetOpenAiApiKey()),
            _ => new WindowsSpeechSynthesisProvider()
        };
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

    private void VoiceSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VoiceSpeedValueText is not null)
            VoiceSpeedValueText.Text = $"{e.NewValue:0.00}×";
    }

    private async void RefreshProviders_Click(object sender, RoutedEventArgs e) =>
        await RefreshProviderStateAsync();

    private async void ProviderSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        await RefreshProviderStateAsync();

    private void OpenAiApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e) =>
        _ = RefreshProviderStateAsync();

    private async Task RefreshProviderStateAsync()
    {
        if (!IsLoaded)
            return;
        var key = GetOpenAiApiKey();
        var windows = await new WindowsSpeechSynthesisProvider().GetAvailabilityAsync();
        var image = await new OpenAiImageGenerationProvider(key).GetAvailabilityAsync();
        var video = await new OpenAiVideoGenerationProvider(key).GetAvailabilityAsync();
        var speech = await new OpenAiSpeechSynthesisProvider(key).GetAvailabilityAsync();
        var keySource = !string.IsNullOrWhiteSpace(OpenAiApiKeyBox.Password)
            ? "API key ชั่วคราวในหน้าต่าง"
            : !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY"))
                ? "OPENAI_API_KEY ของ Windows"
                : "ยังไม่มี API key";
        ProviderStatusText.Text =
            $"Local Voice: {(windows.IsReady ? "พร้อม" : "ไม่พร้อม")} | " +
            $"OpenAI Image/Video/Voice: {(image.IsReady && video.IsReady && speech.IsReady ? "พร้อม" : "ยังไม่พร้อม")} | {keySource}";
    }

    private string SelectedPlatformId() => SelectedTag(PlatformComboBox, "tiktok");

    private string SelectedLanguage()
    {
        var selected = SelectedTag(LanguageComboBox, "auto");
        return _languages.Normalize(selected, ScriptTextBox.Text);
    }

    private string SelectedVisualMode() => SelectedTag(VisualModeComboBox, "image_missing");

    private string SelectedImageQuality() => SelectedTag(ImageQualityComboBox, "medium");

    private int SelectedVideoSeconds() =>
        int.TryParse(SelectedTag(VideoSecondsComboBox, "4"), out var value) ? value : 4;

    private string SelectedVoice()
    {
        var value = VoiceComboBox.Text?.Trim();
        return string.IsNullOrWhiteSpace(value) ? "auto" : value;
    }

    private string? GetOpenAiApiKey()
    {
        var value = OpenAiApiKeyBox.Password?.Trim();
        return string.IsNullOrWhiteSpace(value)
            ? Environment.GetEnvironmentVariable("OPENAI_API_KEY")?.Trim()
            : value;
    }

    private bool RowsRequireGeneration()
    {
        var mode = SelectedVisualMode();
        return mode switch
        {
            "local" => false,
            "image_missing" => _sceneRows.Any(row => string.IsNullOrWhiteSpace(row.AssetPath)),
            _ => _sceneRows.Any(row => row.VisualSourceDisplay is not ("OpenAI Image" or "OpenAI Video"))
        };
    }

    private void UpdatePlanSummary()
    {
        if (_plan is null)
            return;
        var withAssets = _sceneRows.Count(row => !string.IsNullOrWhiteSpace(row.AssetPath) && File.Exists(row.AssetPath));
        var generated = _sceneRows.Count(row => row.VisualSourceDisplay is "OpenAI Image" or "OpenAI Video");
        PlanSummaryText.Text =
            $"{_plan.Scenes.Count} ฉาก · ประมาณ {_plan.EstimatedDurationSeconds:0.0} วินาที · มี Visual {withAssets} ฉาก · AI Generated {generated} ฉาก";
    }

    private string CurrentPlanFingerprint() => string.Join(
        "|",
        TitleTextBox.Text,
        ScriptTextBox.Text,
        SelectedPlatformId(),
        AutoVisualCheckBox.IsChecked == true,
        SequentialFallbackCheckBox.IsChecked == true,
        AssetsDirectoryTextBox.Text);

    private void SetBusy(bool busy, string message)
    {
        PlanButton.IsEnabled = !busy;
        GenerateVisualsButton.IsEnabled = !busy;
        CreateButton.IsEnabled = !busy;
        BusyProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = message;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _operationCancellation?.Cancel();
        if (CreatedJob is null)
            DialogResult = false;
        else
            Close();
    }

    private static string SelectedTag(ComboBox comboBox, string fallback) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;

    private static bool IsImage(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp";

    private static string SanitizeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "script-video" : cleaned;
    }

    private sealed class SceneRow : INotifyPropertyChanged
    {
        private string _prompt;
        private string? _assetPath;
        private string _visualSourceDisplay;
        private string _status;

        public SceneRow(ScriptVideoScene scene, string prompt, string? assetPath)
        {
            SceneIndex = scene.Index;
            DisplayIndex = scene.Index + 1;
            Heading = scene.Heading;
            Narration = scene.Narration;
            DurationDisplay = $"{scene.DurationSeconds:0.0}s";
            _prompt = prompt;
            _assetPath = assetPath;
            _visualSourceDisplay = scene.VisualSource switch
            {
                "keyword_match" => "จับคู่คำ",
                "sequence_fallback" => "เรียงลำดับ",
                _ => string.IsNullOrWhiteSpace(assetPath) ? "Motion Text" : "Project Asset"
            };
            _status = string.IsNullOrWhiteSpace(assetPath) ? "รอ Visual" : "พร้อม";
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public int SceneIndex { get; }
        public int DisplayIndex { get; }
        public string Heading { get; }
        public string Narration { get; }
        public string DurationDisplay { get; }

        public string Prompt
        {
            get => _prompt;
            set
            {
                if (string.Equals(_prompt, value, StringComparison.Ordinal))
                    return;
                _prompt = value;
                OnPropertyChanged();
            }
        }

        public string? AssetPath => _assetPath;
        public string AssetDisplayName => string.IsNullOrWhiteSpace(_assetPath) ? "Motion Text" : Path.GetFileName(_assetPath);

        public string VisualSourceDisplay
        {
            get => _visualSourceDisplay;
            private set
            {
                _visualSourceDisplay = value;
                OnPropertyChanged();
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged();
            }
        }

        public void SetAsset(string path, string source, string status)
        {
            _assetPath = path;
            VisualSourceDisplay = source;
            Status = status;
            OnPropertyChanged(nameof(AssetPath));
            OnPropertyChanged(nameof(AssetDisplayName));
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
