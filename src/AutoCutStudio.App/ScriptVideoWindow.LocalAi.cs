using System.Windows;
using System.Windows.Controls;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;
using AutoCutStudio.Infrastructure;
using Microsoft.Win32;

namespace AutoCutStudio.App;

public partial class ScriptVideoWindow
{
    private readonly LocalAiSettingsStore _localAiSettingsStore = new();
    private LocalAiSettings _localAiSettings = new();

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        try
        {
            _localAiSettings = await _localAiSettingsStore.LoadAsync();
            ApplyLocalAiSettings(_localAiSettings);
            await RefreshLocalProviderStateAsync();
        }
        catch (Exception exception)
        {
            LocalProviderStatusText.Text = exception.Message;
        }
    }

    private async void GenerateVisualsLocalAware_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTag(VisualProviderComboBox, "comfyui") == "openai")
        {
            GenerateVisuals_Click(sender, e);
            return;
        }

        if (_operationCancellation is not null)
            return;
        _operationCancellation = new CancellationTokenSource();
        try
        {
            SetBusy(true, "กำลังส่ง Workflow ไปยัง ComfyUI Local...");
            EnsureCurrentPlan();
            await GenerateLocalVisualsCoreAsync(_operationCancellation.Token);
            StatusText.Text = "ComfyUI สร้าง Visuals เสร็จแล้ว ตรวจแต่ละฉากก่อนสร้างวิดีโอรวม";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "ยกเลิกการสร้าง Local AI Visuals แล้ว";
        }
        catch (Exception exception)
        {
            StatusText.Text = "สร้าง Local AI Visuals ไม่สำเร็จ";
            MessageBox.Show(exception.Message, "ComfyUI Local ไม่สำเร็จ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _operationCancellation.Dispose();
            _operationCancellation = null;
            SetBusy(false, StatusText.Text);
        }
    }

    private async Task GenerateLocalVisualsCoreAsync(CancellationToken cancellationToken)
    {
        var mode = SelectedVisualMode();
        if (mode == "local")
        {
            StatusText.Text = "โหมดนี้ใช้เฉพาะไฟล์ใน Project จึงไม่เรียก ComfyUI";
            return;
        }

        _localAiSettings = ReadLocalAiSettings();
        var candidates = _sceneRows
            .Where(row => mode != "image_missing" || string.IsNullOrWhiteSpace(row.AssetPath))
            .ToList();
        if (candidates.Count == 0)
        {
            StatusText.Text = "ทุกฉากมี Visual แล้ว";
            return;
        }

        IImageGenerationProvider imageProvider = new ComfyUiImageGenerationProvider(_localAiSettings);
        IVideoGenerationProvider videoProvider = new ComfyUiVideoGenerationProvider(_localAiSettings);
        var availability = mode.StartsWith("video", StringComparison.Ordinal)
            ? await videoProvider.GetAvailabilityAsync(cancellationToken)
            : await imageProvider.GetAvailabilityAsync(cancellationToken);
        if (!availability.IsReady)
            throw new InvalidOperationException(availability.Message);

        var preset = new PlatformPresetCatalog().Get(SelectedPlatformId());
        var outputDirectory = Path.Combine(
            _project.RootPath,
            "assets",
            "generated",
            "script-video",
            DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-comfyui-" + SanitizeName(TitleTextBox.Text));
        Directory.CreateDirectory(outputDirectory);

        for (var index = 0; index < candidates.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = candidates[index];
            row.Status = $"ComfyUI {index + 1}/{candidates.Count}";
            StatusText.Text = $"ComfyUI กำลังสร้างฉาก {row.DisplayIndex}/{_sceneRows.Count}: {row.Heading}";

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
                        Model = "comfyui-local"
                    }, cancellationToken);
                    row.SetAsset(result.Path, "ComfyUI Video", "สร้างวิดีโอ Local แล้ว");
                    continue;
                }
                catch (Exception exception) when (mode == "video_fallback" && exception is not OperationCanceledException)
                {
                    row.Status = "วิดีโอล้มเหลว กำลังเจนภาพ Local แทน";
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
                Model = "comfyui-local"
            }, cancellationToken);
            row.SetAsset(image.Path, "ComfyUI Image", "สร้างภาพ Local แล้ว");
        }
        UpdatePlanSummary();
    }

    private async void CreateLocalAware_Click(object sender, RoutedEventArgs e)
    {
        var localVisual = SelectedTag(VisualProviderComboBox, "comfyui") == "comfyui";
        var piperVoice = SelectedTag(VoiceProviderComboBox, "windows") == "piper";
        if (!localVisual && !piperVoice)
        {
            Create_Click(sender, e);
            return;
        }

        if (_operationCancellation is not null)
            return;
        _operationCancellation = new CancellationTokenSource();
        try
        {
            SetBusy(true, "กำลังเตรียม Local AI Visuals และเสียงหลายภาษา...");
            EnsureCurrentPlan();
            if (SelectedVisualMode() != "local" && RowsRequireGeneration())
            {
                if (localVisual)
                    await GenerateLocalVisualsCoreAsync(_operationCancellation.Token);
                else
                    await GenerateVisualsCoreAsync(confirmCost: true, _operationCancellation.Token);
            }

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

            _localAiSettings = ReadLocalAiSettings();
            var speechProvider = CreateLocalAwareSpeechProvider();
            SpeechSynthesisRequest? speechRequest = null;
            if (speechProvider is not null)
            {
                var availability = await speechProvider.GetAvailabilityAsync(_operationCancellation.Token);
                if (!availability.IsReady)
                    throw new InvalidOperationException(availability.Message);
                var voiceProvider = SelectedTag(VoiceProviderComboBox, "windows");
                speechRequest = new SpeechSynthesisRequest
                {
                    Language = language,
                    Voice = SelectedVoice(),
                    Speed = VoiceSpeedSlider.Value,
                    Rate = (int)Math.Round((VoiceSpeedSlider.Value - 1.0) * 6.0),
                    Volume = 100,
                    Model = voiceProvider switch
                    {
                        "piper" => Path.GetFileName(_localAiSettings.PiperModelPath),
                        "openai" => "gpt-4o-mini-tts",
                        _ => "windows-sapi"
                    },
                    Instructions = $"Natural, clear narration in {_languages.DisplayName(language)}."
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

    private ISpeechSynthesisProvider? CreateLocalAwareSpeechProvider()
    {
        if (VoiceoverCheckBox.IsChecked != true)
            return null;
        return SelectedTag(VoiceProviderComboBox, "windows") switch
        {
            "none" => null,
            "piper" => new PiperSpeechSynthesisProvider(_localAiSettings),
            "openai" => new OpenAiSpeechSynthesisProvider(GetOpenAiApiKey()),
            _ => new WindowsSpeechSynthesisProvider()
        };
    }

    private void BrowseImageWorkflow_Click(object sender, RoutedEventArgs e) =>
        BrowseLocalFile(ImageWorkflowTextBox, "ComfyUI API workflow|*.json|JSON files|*.json|All files|*.*");

    private void BrowseVideoWorkflow_Click(object sender, RoutedEventArgs e) =>
        BrowseLocalFile(VideoWorkflowTextBox, "ComfyUI API workflow|*.json|JSON files|*.json|All files|*.*");

    private void BrowsePiperExecutable_Click(object sender, RoutedEventArgs e) =>
        BrowseLocalFile(PiperExecutableTextBox, "Piper executable|piper.exe|Executable files|*.exe|All files|*.*");

    private void BrowsePiperModel_Click(object sender, RoutedEventArgs e) =>
        BrowseLocalFile(PiperModelTextBox, "Piper voice model|*.onnx|ONNX models|*.onnx|All files|*.*");

    private void BrowseLocalFile(TextBox target, string filter)
    {
        var dialog = new OpenFileDialog
        {
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
            target.Text = dialog.FileName;
    }

    private async void SaveLocalAiSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _localAiSettings = ReadLocalAiSettings();
            await _localAiSettingsStore.SaveAsync(_localAiSettings);
            StatusText.Text = $"บันทึก Local AI settings แล้ว: {_localAiSettingsStore.SettingsPath}";
            await RefreshLocalProviderStateAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "บันทึก Local AI settings ไม่สำเร็จ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyLocalAiSettings(LocalAiSettings settings)
    {
        ComfyUiUrlTextBox.Text = settings.ComfyUiBaseUrl;
        ImageWorkflowTextBox.Text = settings.ImageWorkflowPath;
        VideoWorkflowTextBox.Text = settings.VideoWorkflowPath;
        PiperExecutableTextBox.Text = settings.PiperExecutablePath;
        PiperModelTextBox.Text = settings.PiperModelPath;
    }

    private LocalAiSettings ReadLocalAiSettings() => new()
    {
        ComfyUiBaseUrl = string.IsNullOrWhiteSpace(ComfyUiUrlTextBox.Text)
            ? "http://127.0.0.1:8188"
            : ComfyUiUrlTextBox.Text.Trim(),
        ImageWorkflowPath = ImageWorkflowTextBox.Text.Trim(),
        VideoWorkflowPath = VideoWorkflowTextBox.Text.Trim(),
        PiperExecutablePath = PiperExecutableTextBox.Text.Trim(),
        PiperModelPath = PiperModelTextBox.Text.Trim(),
        PiperConfigPath = string.Empty,
        ComfyUiTimeoutMinutes = 30
    };

    private async Task RefreshLocalProviderStateAsync()
    {
        _localAiSettings = ReadLocalAiSettings();
        var piper = await SafeLocalAvailabilityAsync(() =>
            new PiperSpeechSynthesisProvider(_localAiSettings).GetAvailabilityAsync());
        var image = await SafeLocalAvailabilityAsync(() =>
            new ComfyUiImageGenerationProvider(_localAiSettings).GetAvailabilityAsync());
        var video = await SafeLocalAvailabilityAsync(() =>
            new ComfyUiVideoGenerationProvider(_localAiSettings).GetAvailabilityAsync());
        LocalProviderStatusText.Text =
            $"Piper: {(piper.IsReady ? "พร้อม" : piper.Message)} | " +
            $"ComfyUI Image: {(image.IsReady ? "พร้อม" : image.Message)} | " +
            $"ComfyUI Video: {(video.IsReady ? "พร้อม" : video.Message)}";
    }

    private static async Task<ProviderAvailability> SafeLocalAvailabilityAsync(
        Func<Task<ProviderAvailability>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception exception)
        {
            return new ProviderAvailability
            {
                IsReady = false,
                Status = "error",
                Message = exception.Message
            };
        }
    }
}
