using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AutoCutStudio.Core.Services;

namespace AutoCutStudio.Infrastructure;

public sealed record LocalAiSettings
{
    public string ComfyUiBaseUrl { get; init; } = "http://127.0.0.1:8188";
    public string ImageWorkflowPath { get; init; } = string.Empty;
    public string VideoWorkflowPath { get; init; } = string.Empty;
    public string PiperExecutablePath { get; init; } = string.Empty;
    public string PiperModelPath { get; init; } = string.Empty;
    public string PiperConfigPath { get; init; } = string.Empty;
    public int ComfyUiTimeoutMinutes { get; init; } = 30;
}

public sealed class LocalAiSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoCutStudio",
        "local-ai-settings.json");

    public async Task<LocalAiSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsPath))
            return new LocalAiSettings();

        try
        {
            await using var stream = File.OpenRead(SettingsPath);
            return await JsonSerializer.DeserializeAsync<LocalAiSettings>(stream, JsonOptions, cancellationToken)
                   ?? new LocalAiSettings();
        }
        catch
        {
            return new LocalAiSettings();
        }
    }

    public async Task SaveAsync(LocalAiSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var temporary = SettingsPath + ".tmp";
        await using (var stream = File.Create(temporary))
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
        File.Move(temporary, SettingsPath, true);
    }
}

public sealed class PiperSpeechSynthesisProvider : ISpeechSynthesisProvider
{
    private readonly LocalAiSettings _settings;

    public PiperSpeechSynthesisProvider(LocalAiSettings settings) => _settings = settings;

    public string ProviderId => "piper_local";
    public string DisplayName => "Piper Local Voice";

    public Task<ProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        var executable = ResolveExecutablePath(_settings.PiperExecutablePath);
        var model = ResolveModelPath(_settings.PiperModelPath, "auto");
        var ready = executable is not null && model is not null;
        var message = executable is null
            ? "ยังไม่พบ piper.exe กรุณาเลือกไฟล์โปรแกรม Piper"
            : model is null
                ? "ยังไม่พบโมเดลเสียง Piper (.onnx) กรุณาเลือกโมเดลของภาษาที่ต้องการ"
                : $"พร้อมใช้ {Path.GetFileName(model)} โดยไม่ส่งข้อความออกจากเครื่อง";
        return Task.FromResult(new ProviderAvailability
        {
            ProviderId = ProviderId,
            DisplayName = DisplayName,
            IsReady = ready,
            Status = ready ? "ready" : "missing_dependency",
            Message = message
        });
    }

    public async Task<GeneratedMediaAsset> GenerateAsync(
        SpeechSynthesisRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            throw new ArgumentException("ข้อความเสียงว่างเปล่า", nameof(request));

        var executable = ResolveExecutablePath(_settings.PiperExecutablePath)
                         ?? throw new FileNotFoundException("ไม่พบ piper.exe", _settings.PiperExecutablePath);
        var model = ResolveModelPath(_settings.PiperModelPath, request.Voice)
                    ?? throw new FileNotFoundException("ไม่พบโมเดล Piper (.onnx)", _settings.PiperModelPath);
        var output = Path.GetFullPath(request.OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        if (File.Exists(output))
            throw new IOException($"ไฟล์เสียงปลายทางมีอยู่แล้ว: {output}");

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add("--model");
        startInfo.ArgumentList.Add(model);
        var config = ResolveConfigPath(model, _settings.PiperConfigPath);
        if (config is not null)
        {
            startInfo.ArgumentList.Add("--config");
            startInfo.ArgumentList.Add(config);
        }
        startInfo.ArgumentList.Add("--output_file");
        startInfo.ArgumentList.Add(output);
        startInfo.ArgumentList.Add("--length_scale");
        startInfo.ArgumentList.Add((1.0 / Math.Clamp(request.Speed, 0.5, 2.0)).ToString("0.###", CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--sentence_silence");
        startInfo.ArgumentList.Add("0.15");

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("ไม่สามารถเริ่ม Piper ได้");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.StandardInput.WriteAsync(request.Text.AsMemory(), cancellationToken);
        await process.StandardInput.WriteLineAsync();
        process.StandardInput.Close();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
            if (!process.HasExited)
                process.Kill(true);
            throw;
        }
        _ = await stdoutTask;
        var error = await stderrTask;
        if (process.ExitCode != 0 || !File.Exists(output) || new FileInfo(output).Length < 1000)
        {
            TryDelete(output);
            throw new InvalidDataException($"Piper สร้างเสียงไม่สำเร็จ (exit {process.ExitCode}): {Tail(error, 12)}");
        }

        return new GeneratedMediaAsset
        {
            ProviderId = ProviderId,
            Kind = "audio",
            Path = output,
            Prompt = request.Text,
            Model = Path.GetFileName(model)
        };
    }

    public static string? ResolveExecutablePath(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return Path.GetFullPath(configured);
        var bundled = Path.Combine(AppContext.BaseDirectory, "tools", "piper", "piper.exe");
        return File.Exists(bundled) ? bundled : null;
    }

    public static string? ResolveModelPath(string? configured, string? requestedVoice)
    {
        if (!string.IsNullOrWhiteSpace(requestedVoice) &&
            !string.Equals(requestedVoice, "auto", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(requestedVoice))
            return Path.GetFullPath(requestedVoice);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return Path.GetFullPath(configured);
        return null;
    }

    private static string? ResolveConfigPath(string model, string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return Path.GetFullPath(configured);
        var adjacent = model + ".json";
        return File.Exists(adjacent) ? adjacent : null;
    }

    private static string Tail(string value, int count) => string.Join(
        Environment.NewLine,
        value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).TakeLast(count));

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}

public sealed class ComfyUiImageGenerationProvider : IImageGenerationProvider
{
    private readonly LocalAiSettings _settings;

    public ComfyUiImageGenerationProvider(LocalAiSettings settings) => _settings = settings;

    public string ProviderId => "comfyui_image_local";
    public string DisplayName => "ComfyUI Local Image";

    public Task<ProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
        new ComfyUiClient(_settings).GetAvailabilityAsync(
            ProviderId,
            DisplayName,
            _settings.ImageWorkflowPath,
            cancellationToken);

    public async Task<GeneratedMediaAsset> GenerateAsync(
        ImageGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request.Prompt, request.OutputPath);
        var workflow = await File.ReadAllTextAsync(
            RequireWorkflow(_settings.ImageWorkflowPath, "ภาพ"),
            cancellationToken);
        var prepared = ComfyUiWorkflowTemplate.Prepare(
            workflow,
            request.Prompt,
            request.Width,
            request.Height,
            durationSeconds: 0,
            inputImageName: null,
            seed: Random.Shared.NextInt64(1, long.MaxValue),
            outputPrefix: "AutoCutStudio-image");
        var output = await new ComfyUiClient(_settings).QueueAndDownloadAsync(
            prepared,
            request.OutputPath,
            "image",
            referenceImagePath: null,
            cancellationToken);
        return new GeneratedMediaAsset
        {
            ProviderId = ProviderId,
            Kind = "image",
            Path = output,
            Prompt = request.Prompt,
            Model = "comfyui-workflow:" + Path.GetFileName(_settings.ImageWorkflowPath)
        };
    }

    private static void ValidateRequest(string prompt, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Prompt ว่างเปล่า");
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("ไม่ได้ระบุ Output path");
    }

    private static string RequireWorkflow(string path, string kind) =>
        File.Exists(path) ? Path.GetFullPath(path) : throw new FileNotFoundException($"ไม่พบ ComfyUI workflow สำหรับ{kind}", path);
}

public sealed class ComfyUiVideoGenerationProvider : IVideoGenerationProvider
{
    private readonly LocalAiSettings _settings;

    public ComfyUiVideoGenerationProvider(LocalAiSettings settings) => _settings = settings;

    public string ProviderId => "comfyui_video_local";
    public string DisplayName => "ComfyUI Local Video";

    public Task<ProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
        new ComfyUiClient(_settings).GetAvailabilityAsync(
            ProviderId,
            DisplayName,
            _settings.VideoWorkflowPath,
            cancellationToken);

    public async Task<GeneratedMediaAsset> GenerateAsync(
        VideoGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new ArgumentException("Prompt ว่างเปล่า");
        var workflowPath = File.Exists(_settings.VideoWorkflowPath)
            ? Path.GetFullPath(_settings.VideoWorkflowPath)
            : throw new FileNotFoundException("ไม่พบ ComfyUI workflow สำหรับวิดีโอ", _settings.VideoWorkflowPath);
        var client = new ComfyUiClient(_settings);
        var uploadedName = !string.IsNullOrWhiteSpace(request.ReferenceImagePath) && File.Exists(request.ReferenceImagePath)
            ? await client.UploadImageAsync(request.ReferenceImagePath, cancellationToken)
            : null;
        var workflow = await File.ReadAllTextAsync(workflowPath, cancellationToken);
        var prepared = ComfyUiWorkflowTemplate.Prepare(
            workflow,
            request.Prompt,
            request.Width,
            request.Height,
            request.DurationSeconds,
            uploadedName,
            Random.Shared.NextInt64(1, long.MaxValue),
            "AutoCutStudio-video");
        var output = await client.QueueAndDownloadAsync(
            prepared,
            request.OutputPath,
            "video",
            request.ReferenceImagePath,
            cancellationToken);
        return new GeneratedMediaAsset
        {
            ProviderId = ProviderId,
            Kind = "video",
            Path = output,
            Prompt = request.Prompt,
            Model = "comfyui-workflow:" + Path.GetFileName(workflowPath)
        };
    }
}

public static class ComfyUiWorkflowTemplate
{
    public static JsonObject Prepare(
        string workflowJson,
        string prompt,
        int width,
        int height,
        int durationSeconds,
        string? inputImageName,
        long seed,
        string outputPrefix)
    {
        if (string.IsNullOrWhiteSpace(workflowJson))
            throw new InvalidDataException("ComfyUI workflow ว่างเปล่า");

        var frames = Math.Max(1, Math.Max(1, durationSeconds) * 16);
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["{{PROMPT}}"] = EscapeJsonString(prompt),
            ["{{NEGATIVE_PROMPT}}"] = EscapeJsonString("text, watermark, logo, low quality, distorted anatomy"),
            ["{{WIDTH}}"] = Math.Clamp(width, 64, 4096).ToString(CultureInfo.InvariantCulture),
            ["{{HEIGHT}}"] = Math.Clamp(height, 64, 4096).ToString(CultureInfo.InvariantCulture),
            ["{{SEED}}"] = seed.ToString(CultureInfo.InvariantCulture),
            ["{{DURATION_SECONDS}}"] = Math.Max(1, durationSeconds).ToString(CultureInfo.InvariantCulture),
            ["{{FRAMES}}"] = frames.ToString(CultureInfo.InvariantCulture),
            ["{{FPS}}"] = "16",
            ["{{INPUT_IMAGE}}"] = EscapeJsonString(inputImageName ?? string.Empty),
            ["{{OUTPUT_PREFIX}}"] = EscapeJsonString(outputPrefix)
        };
        var expanded = workflowJson;
        foreach (var pair in replacements)
            expanded = expanded.Replace(pair.Key, pair.Value, StringComparison.Ordinal);

        var root = JsonNode.Parse(expanded) as JsonObject
                   ?? throw new InvalidDataException("ComfyUI workflow ต้องเป็น JSON object ใน API format");
        InjectKnownInputs(root, prompt, width, height, frames, inputImageName, seed, outputPrefix);
        return root;
    }

    public static IReadOnlyList<ComfyUiOutputReference> ExtractOutputReferences(JsonNode history, string kind)
    {
        var results = new List<ComfyUiOutputReference>();
        Visit(history, propertyName: null, results);
        var extensions = string.Equals(kind, "video", StringComparison.OrdinalIgnoreCase)
            ? new HashSet<string>([".mp4", ".webm", ".mov", ".mkv"], StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>([".png", ".jpg", ".jpeg", ".webp", ".bmp"], StringComparer.OrdinalIgnoreCase);
        return results
            .Where(item => extensions.Contains(Path.GetExtension(item.FileName)))
            .DistinctBy(item => (item.FileName, item.Subfolder, item.Type))
            .ToList();
    }

    private static void Visit(JsonNode? node, string? propertyName, ICollection<ComfyUiOutputReference> output)
    {
        if (node is JsonObject obj)
        {
            if (obj["filename"] is JsonValue filenameValue && filenameValue.TryGetValue<string>(out var filename) &&
                !string.IsNullOrWhiteSpace(filename))
            {
                var subfolder = obj["subfolder"]?.GetValue<string>() ?? string.Empty;
                var type = obj["type"]?.GetValue<string>() ?? "output";
                output.Add(new ComfyUiOutputReference(filename, subfolder, type));
            }
            foreach (var pair in obj)
                Visit(pair.Value, pair.Key, output);
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
                Visit(item, propertyName, output);
        }
    }

    private static void InjectKnownInputs(
        JsonObject root,
        string prompt,
        int width,
        int height,
        int frames,
        string? inputImageName,
        long seed,
        string outputPrefix)
    {
        var promptInjected = false;
        foreach (var pair in root)
        {
            if (pair.Value is not JsonObject node || node["inputs"] is not JsonObject inputs)
                continue;
            var classType = node["class_type"]?.GetValue<string>() ?? string.Empty;
            var title = node["_meta"]?["title"]?.GetValue<string>() ?? string.Empty;

            if (!promptInjected && inputs["text"] is JsonValue &&
                (classType.Contains("TextEncode", StringComparison.OrdinalIgnoreCase) ||
                 classType.Contains("Prompt", StringComparison.OrdinalIgnoreCase)) &&
                !title.Contains("negative", StringComparison.OrdinalIgnoreCase))
            {
                inputs["text"] = prompt;
                promptInjected = true;
            }
            SetNumberIfPresent(inputs, "width", Math.Clamp(width, 64, 4096));
            SetNumberIfPresent(inputs, "height", Math.Clamp(height, 64, 4096));
            SetNumberIfPresent(inputs, "seed", seed);
            SetNumberIfPresent(inputs, "noise_seed", seed);
            SetNumberIfPresent(inputs, "frames", frames);
            SetNumberIfPresent(inputs, "num_frames", frames);
            SetNumberIfPresent(inputs, "length", frames);
            if (inputs.ContainsKey("filename_prefix"))
                inputs["filename_prefix"] = outputPrefix;
            if (!string.IsNullOrWhiteSpace(inputImageName) &&
                inputs.ContainsKey("image") &&
                classType.Contains("LoadImage", StringComparison.OrdinalIgnoreCase))
                inputs["image"] = inputImageName;
        }
    }

    private static void SetNumberIfPresent(JsonObject inputs, string key, long value)
    {
        if (inputs.ContainsKey(key) && inputs[key] is JsonValue)
            inputs[key] = value;
    }

    private static string EscapeJsonString(string value)
    {
        var serialized = JsonSerializer.Serialize(value);
        return serialized.Length >= 2 ? serialized[1..^1] : string.Empty;
    }
}

public sealed record ComfyUiOutputReference(string FileName, string Subfolder, string Type);

internal sealed class ComfyUiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly LocalAiSettings _settings;
    private readonly Uri _baseUri;

    public ComfyUiClient(LocalAiSettings settings)
    {
        _settings = settings;
        _baseUri = ValidateLocalBaseUrl(settings.ComfyUiBaseUrl);
    }

    public async Task<ProviderAvailability> GetAvailabilityAsync(
        string providerId,
        string displayName,
        string workflowPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(workflowPath))
        {
            return new ProviderAvailability
            {
                ProviderId = providerId,
                DisplayName = displayName,
                IsReady = false,
                Status = "missing_workflow",
                Message = "ยังไม่ได้เลือก ComfyUI workflow แบบ Export (API)"
            };
        }

        try
        {
            using var client = CreateHttpClient(TimeSpan.FromSeconds(4));
            using var response = await client.GetAsync(new Uri(_baseUri, "system_stats"), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                using var fallback = await client.GetAsync(new Uri(_baseUri, "object_info"), cancellationToken);
                fallback.EnsureSuccessStatusCode();
            }
            return new ProviderAvailability
            {
                ProviderId = providerId,
                DisplayName = displayName,
                IsReady = true,
                Status = "ready",
                Message = $"เชื่อม ComfyUI Local แล้ว: {_baseUri}"
            };
        }
        catch (Exception exception)
        {
            return new ProviderAvailability
            {
                ProviderId = providerId,
                DisplayName = displayName,
                IsReady = false,
                Status = "offline",
                Message = $"ยังเชื่อม ComfyUI ไม่ได้: {exception.Message}"
            };
        }
    }

    public async Task<string> UploadImageAsync(string path, CancellationToken cancellationToken)
    {
        using var client = CreateHttpClient(TimeSpan.FromMinutes(5));
        await using var stream = File.OpenRead(path);
        using var content = new MultipartFormDataContent();
        using var file = new StreamContent(stream);
        file.Headers.ContentType = new MediaTypeHeaderValue(ImageMimeType(path));
        content.Add(file, "image", Path.GetFileName(path));
        content.Add(new StringContent("input"), "type");
        content.Add(new StringContent("true"), "overwrite");
        using var response = await client.PostAsync(new Uri(_baseUri, "upload/image"), content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"ComfyUI upload image ล้มเหลว {(int)response.StatusCode}: {body}");
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("name", out var name)
                ? name.GetString() ?? Path.GetFileName(path)
                : Path.GetFileName(path);
        }
        catch
        {
            return Path.GetFileName(path);
        }
    }

    public async Task<string> QueueAndDownloadAsync(
        JsonObject workflow,
        string outputPath,
        string kind,
        string? referenceImagePath,
        CancellationToken cancellationToken)
    {
        var clientId = Guid.NewGuid().ToString("N");
        var payload = new JsonObject
        {
            ["prompt"] = workflow.DeepClone(),
            ["client_id"] = clientId
        };
        string promptId;
        using (var client = CreateHttpClient(TimeSpan.FromMinutes(3)))
        using (var content = new StringContent(payload.ToJsonString(JsonOptions), Encoding.UTF8, "application/json"))
        using (var response = await client.PostAsync(new Uri(_baseUri, "prompt"), content, cancellationToken))
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"ComfyUI queue prompt ล้มเหลว {(int)response.StatusCode}: {body}");
            using var document = JsonDocument.Parse(body);
            promptId = document.RootElement.TryGetProperty("prompt_id", out var id)
                ? id.GetString() ?? string.Empty
                : string.Empty;
            if (string.IsNullOrWhiteSpace(promptId))
                throw new InvalidDataException("ComfyUI ไม่ได้ส่ง prompt_id กลับมา");
        }

        var deadline = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(_settings.ComfyUiTimeoutMinutes, 1, 180));
        JsonNode? completedHistory = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            using var client = CreateHttpClient(TimeSpan.FromSeconds(20));
            using var response = await client.GetAsync(
                new Uri(_baseUri, "history/" + Uri.EscapeDataString(promptId)),
                cancellationToken);
            if (!response.IsSuccessStatusCode)
                continue;
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var root = JsonNode.Parse(body);
            var entry = root?[promptId];
            if (entry is null)
                continue;
            var statusText = entry["status"]?["status_str"]?.GetValue<string>();
            if (string.Equals(statusText, "error", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("ComfyUI workflow ล้มเหลว: " + entry["status"]?.ToJsonString());
            if (entry["outputs"] is not null)
            {
                completedHistory = entry;
                break;
            }
        }
        if (completedHistory is null)
            throw new TimeoutException("ComfyUI ใช้เวลานานเกินกำหนด");

        var references = ComfyUiWorkflowTemplate.ExtractOutputReferences(completedHistory, kind);
        if (references.Count == 0)
            throw new InvalidDataException($"ComfyUI workflow เสร็จแล้วแต่ไม่พบไฟล์ {kind} ที่รองรับ");
        var selected = references[^1];
        var query = $"view?filename={Uri.EscapeDataString(selected.FileName)}&subfolder={Uri.EscapeDataString(selected.Subfolder)}&type={Uri.EscapeDataString(selected.Type)}";
        var output = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using (var client = CreateHttpClient(TimeSpan.FromMinutes(15)))
        using (var response = await client.GetAsync(new Uri(_baseUri, query), HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var file = File.Create(output);
            await input.CopyToAsync(file, cancellationToken);
        }
        var minimum = string.Equals(kind, "video", StringComparison.OrdinalIgnoreCase) ? 4096 : 1024;
        if (!File.Exists(output) || new FileInfo(output).Length < minimum)
        {
            TryDelete(output);
            throw new InvalidDataException("ไฟล์ที่ดาวน์โหลดจาก ComfyUI ไม่สมบูรณ์");
        }
        return output;
    }

    private HttpClient CreateHttpClient(TimeSpan timeout) => new()
    {
        BaseAddress = _baseUri,
        Timeout = timeout
    };

    private static Uri ValidateLocalBaseUrl(string value)
    {
        if (!Uri.TryCreate(value?.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
            throw new InvalidDataException("ComfyUI URL ไม่ถูกต้อง");
        var local = string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                    IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address);
        if (!local)
            throw new InvalidDataException("เพื่อความปลอดภัย AutoCut Studio รองรับ ComfyUI ผ่าน localhost เท่านั้น");
        return uri;
    }

    private static string ImageMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "image/jpeg"
    };

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}
