using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AutoCutStudio.Core.Services;

namespace AutoCutStudio.Infrastructure;

public sealed class WindowsSpeechSynthesisProvider : ISpeechSynthesisProvider
{
    public string ProviderId => "windows_sapi";
    public string DisplayName => "Windows Local Voice";

    public Task<ProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ProviderAvailability
        {
            ProviderId = ProviderId,
            DisplayName = DisplayName,
            IsReady = OperatingSystem.IsWindows(),
            Status = OperatingSystem.IsWindows() ? "ready" : "unsupported",
            Message = OperatingSystem.IsWindows()
                ? "พร้อมใช้เสียงที่ติดตั้งใน Windows โดยเลือกตามภาษา/ชื่อเสียง"
                : "Windows Local Voice ใช้ได้เฉพาะ Windows"
        });

    public async Task<GeneratedMediaAsset> GenerateAsync(
        SpeechSynthesisRequest request,
        CancellationToken cancellationToken = default)
    {
        var availability = await GetAvailabilityAsync(cancellationToken);
        if (!availability.IsReady)
            throw new InvalidOperationException(availability.Message);

        var rate = request.Rate != 0
            ? request.Rate
            : (int)Math.Round((Math.Clamp(request.Speed, 0.5, 2.0) - 1.0) * 6.0);
        await new WindowsVoiceoverService().GenerateAsync(
            request.Text,
            request.OutputPath,
            new WindowsVoiceoverOptions
            {
                Language = request.Language,
                PreferredVoiceName = string.Equals(request.Voice, "auto", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : request.Voice,
                Rate = Math.Clamp(rate, -10, 10),
                Volume = Math.Clamp(request.Volume, 0, 100)
            },
            cancellationToken);

        return new GeneratedMediaAsset
        {
            ProviderId = ProviderId,
            Kind = "audio",
            Path = Path.GetFullPath(request.OutputPath),
            Prompt = request.Text,
            Model = "windows-sapi"
        };
    }
}

public sealed class OpenAiImageGenerationProvider : IImageGenerationProvider
{
    private readonly string? _apiKey;
    private readonly string _baseUrl;

    public OpenAiImageGenerationProvider(string? apiKey = null, string? baseUrl = null)
    {
        _apiKey = OpenAiProviderUtility.ResolveApiKey(apiKey);
        _baseUrl = OpenAiProviderUtility.ResolveBaseUrl(baseUrl);
    }

    public string ProviderId => "openai_image";
    public string DisplayName => "OpenAI Image";

    public Task<ProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(OpenAiProviderUtility.Availability(ProviderId, DisplayName, _apiKey));

    public async Task<GeneratedMediaAsset> GenerateAsync(
        ImageGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        OpenAiProviderUtility.ValidatePromptAndOutput(request.Prompt, request.OutputPath);
        OpenAiProviderUtility.EnsureApiKey(_apiKey, DisplayName);
        var size = ScenePromptBuilder.OpenAiImageSize(request.Width, request.Height);
        var quality = request.Quality.Trim().ToLowerInvariant() switch
        {
            "low" => "low",
            "high" => "high",
            _ => "medium"
        };
        using var client = OpenAiProviderUtility.CreateClient(_apiKey!);
        using var payload = new StringContent(
            JsonSerializer.Serialize(new
            {
                model = string.IsNullOrWhiteSpace(request.Model) ? "gpt-image-1" : request.Model,
                prompt = request.Prompt,
                size,
                quality,
                output_format = "png"
            }, OpenAiProviderUtility.JsonOptions),
            Encoding.UTF8,
            "application/json");
        using var response = await client.PostAsync(
            $"{_baseUrl}/images/generations",
            payload,
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        OpenAiProviderUtility.EnsureSuccess(response, body, DisplayName);

        using var document = JsonDocument.Parse(body);
        var data = document.RootElement.GetProperty("data");
        if (data.GetArrayLength() == 0 ||
            !data[0].TryGetProperty("b64_json", out var encodedElement) ||
            string.IsNullOrWhiteSpace(encodedElement.GetString()))
            throw new InvalidDataException("OpenAI Image ไม่ได้ส่งข้อมูลภาพกลับมา");

        var output = Path.GetFullPath(request.OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllBytesAsync(
            output,
            Convert.FromBase64String(encodedElement.GetString()!),
            cancellationToken);
        if (!File.Exists(output) || new FileInfo(output).Length < 1024)
            throw new InvalidDataException("ไฟล์ภาพที่สร้างไม่สมบูรณ์");

        return new GeneratedMediaAsset
        {
            ProviderId = ProviderId,
            Kind = "image",
            Path = output,
            Prompt = request.Prompt,
            Model = string.IsNullOrWhiteSpace(request.Model) ? "gpt-image-1" : request.Model
        };
    }
}

public sealed class OpenAiSpeechSynthesisProvider : ISpeechSynthesisProvider
{
    private readonly string? _apiKey;
    private readonly string _baseUrl;

    public OpenAiSpeechSynthesisProvider(string? apiKey = null, string? baseUrl = null)
    {
        _apiKey = OpenAiProviderUtility.ResolveApiKey(apiKey);
        _baseUrl = OpenAiProviderUtility.ResolveBaseUrl(baseUrl);
    }

    public string ProviderId => "openai_tts";
    public string DisplayName => "OpenAI Multilingual Voice";

    public Task<ProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(OpenAiProviderUtility.Availability(ProviderId, DisplayName, _apiKey));

    public async Task<GeneratedMediaAsset> GenerateAsync(
        SpeechSynthesisRequest request,
        CancellationToken cancellationToken = default)
    {
        OpenAiProviderUtility.ValidatePromptAndOutput(request.Text, request.OutputPath);
        OpenAiProviderUtility.EnsureApiKey(_apiKey, DisplayName);
        var output = Path.GetFullPath(request.OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var parts = SplitText(request.Text, 3800);
        var temporary = new List<string>();
        try
        {
            for (var index = 0; index < parts.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var partPath = parts.Count == 1
                    ? output
                    : Path.Combine(Path.GetDirectoryName(output)!, $"voice-part-{index:000}-{Guid.NewGuid():N}.wav");
                await GeneratePartAsync(request with
                {
                    Text = parts[index],
                    OutputPath = partPath
                }, cancellationToken);
                temporary.Add(partPath);
            }

            if (temporary.Count > 1)
                await ConcatenateAsync(temporary, output, cancellationToken);

            if (!File.Exists(output) || new FileInfo(output).Length < 1000)
                throw new InvalidDataException("OpenAI Voiceover output ไม่สมบูรณ์");
        }
        finally
        {
            foreach (var path in temporary.Where(path => !string.Equals(path, output, StringComparison.OrdinalIgnoreCase)))
                TryDelete(path);
        }

        return new GeneratedMediaAsset
        {
            ProviderId = ProviderId,
            Kind = "audio",
            Path = output,
            Prompt = request.Text,
            Model = string.IsNullOrWhiteSpace(request.Model) ? "gpt-4o-mini-tts" : request.Model
        };
    }

    private async Task GeneratePartAsync(SpeechSynthesisRequest request, CancellationToken cancellationToken)
    {
        using var client = OpenAiProviderUtility.CreateClient(_apiKey!);
        var voice = string.IsNullOrWhiteSpace(request.Voice) || request.Voice == "auto"
            ? "marin"
            : request.Voice.Trim();
        var languageInstruction = string.IsNullOrWhiteSpace(request.Language) || request.Language == "auto"
            ? string.Empty
            : $" Speak naturally in language code {request.Language}.";
        using var payload = new StringContent(
            JsonSerializer.Serialize(new
            {
                model = string.IsNullOrWhiteSpace(request.Model) ? "gpt-4o-mini-tts" : request.Model,
                voice,
                input = request.Text,
                instructions = (request.Instructions + languageInstruction).Trim(),
                response_format = "wav",
                speed = Math.Clamp(request.Speed, 0.25, 4.0)
            }, OpenAiProviderUtility.JsonOptions),
            Encoding.UTF8,
            "application/json");
        using var response = await client.PostAsync(
            $"{_baseUrl}/audio/speech",
            payload,
            cancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = Encoding.UTF8.GetString(bytes);
            OpenAiProviderUtility.EnsureSuccess(response, body, DisplayName);
        }
        await File.WriteAllBytesAsync(request.OutputPath, bytes, cancellationToken);
    }

    private static IReadOnlyList<string> SplitText(string text, int maximumLength)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (normalized.Length <= maximumLength)
            return [normalized];

        var chunks = new List<string>();
        var paragraphs = normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var builder = new StringBuilder();
        foreach (var paragraph in paragraphs)
        {
            if (paragraph.Length > maximumLength)
            {
                if (builder.Length > 0)
                {
                    chunks.Add(builder.ToString().Trim());
                    builder.Clear();
                }
                var remaining = paragraph;
                while (remaining.Length > maximumLength)
                {
                    var split = remaining.LastIndexOfAny(['.', '!', '?', '。', '！', '？', ' '], maximumLength - 1);
                    if (split < maximumLength / 2)
                        split = maximumLength;
                    chunks.Add(remaining[..split].Trim());
                    remaining = remaining[split..].Trim();
                }
                if (remaining.Length > 0)
                    builder.Append(remaining);
                continue;
            }

            if (builder.Length + paragraph.Length + 1 > maximumLength)
            {
                chunks.Add(builder.ToString().Trim());
                builder.Clear();
            }
            if (builder.Length > 0)
                builder.AppendLine();
            builder.Append(paragraph);
        }
        if (builder.Length > 0)
            chunks.Add(builder.ToString().Trim());
        return chunks.Where(item => !string.IsNullOrWhiteSpace(item)).ToList();
    }

    private static async Task ConcatenateAsync(
        IReadOnlyList<string> inputPaths,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var availability = new ToolLocator().Locate();
        if (!availability.IsReady || string.IsNullOrWhiteSpace(availability.FfmpegPath))
            throw new InvalidOperationException("ต้องมี FFmpeg เพื่อรวมเสียงบรรยายหลายส่วน");
        var partial = outputPath + ".concat.wav";
        TryDelete(partial);
        var startInfo = new ProcessStartInfo
        {
            FileName = availability.FfmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-y");
        foreach (var input in inputPaths)
        {
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(input);
        }
        startInfo.ArgumentList.Add("-filter_complex");
        startInfo.ArgumentList.Add(string.Concat(Enumerable.Range(0, inputPaths.Count).Select(index => $"[{index}:a:0]")) +
                                   $"concat=n={inputPaths.Count}:v=0:a=1[aout]");
        startInfo.ArgumentList.Add("-map");
        startInfo.ArgumentList.Add("[aout]");
        startInfo.ArgumentList.Add("-c:a");
        startInfo.ArgumentList.Add("pcm_s16le");
        startInfo.ArgumentList.Add(partial);

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("ไม่สามารถเริ่ม FFmpeg สำหรับรวมเสียงได้");
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var error = await errorTask;
        if (process.ExitCode != 0 || !File.Exists(partial) || new FileInfo(partial).Length < 1000)
        {
            TryDelete(partial);
            throw new InvalidOperationException($"รวมเสียงบรรยายไม่สำเร็จ: {error}");
        }
        File.Move(partial, outputPath, true);
    }

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

public sealed class OpenAiVideoGenerationProvider : IVideoGenerationProvider
{
    private readonly string? _apiKey;
    private readonly string _baseUrl;

    public OpenAiVideoGenerationProvider(string? apiKey = null, string? baseUrl = null)
    {
        _apiKey = OpenAiProviderUtility.ResolveApiKey(apiKey);
        _baseUrl = OpenAiProviderUtility.ResolveBaseUrl(baseUrl);
    }

    public string ProviderId => "openai_video";
    public string DisplayName => "OpenAI Video";

    public Task<ProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(OpenAiProviderUtility.Availability(ProviderId, DisplayName, _apiKey));

    public async Task<GeneratedMediaAsset> GenerateAsync(
        VideoGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        OpenAiProviderUtility.ValidatePromptAndOutput(request.Prompt, request.OutputPath);
        OpenAiProviderUtility.EnsureApiKey(_apiKey, DisplayName);
        var size = ScenePromptBuilder.OpenAiVideoSize(request.Width, request.Height);
        var seconds = request.DurationSeconds <= 4 ? 4 : request.DurationSeconds <= 8 ? 8 : 12;
        string videoId;
        using (var client = OpenAiProviderUtility.CreateClient(_apiKey!))
        using (var form = new MultipartFormDataContent())
        {
            form.Add(new StringContent(string.IsNullOrWhiteSpace(request.Model) ? "sora-2" : request.Model), "model");
            form.Add(new StringContent(request.Prompt), "prompt");
            form.Add(new StringContent(seconds.ToString(CultureInfo.InvariantCulture)), "seconds");
            form.Add(new StringContent(size), "size");
            FileStream? referenceStream = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(request.ReferenceImagePath) && File.Exists(request.ReferenceImagePath))
                {
                    referenceStream = File.OpenRead(request.ReferenceImagePath);
                    var fileContent = new StreamContent(referenceStream);
                    fileContent.Headers.ContentType = new MediaTypeHeaderValue(OpenAiProviderUtility.ImageMimeType(request.ReferenceImagePath));
                    form.Add(fileContent, "input_reference", Path.GetFileName(request.ReferenceImagePath));
                }
                using var response = await client.PostAsync($"{_baseUrl}/videos", form, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                OpenAiProviderUtility.EnsureSuccess(response, body, DisplayName);
                using var document = JsonDocument.Parse(body);
                videoId = document.RootElement.GetProperty("id").GetString()
                          ?? throw new InvalidDataException("OpenAI Video ไม่ได้ส่ง video id กลับมา");
            }
            finally
            {
                referenceStream?.Dispose();
            }
        }

        var completed = false;
        string? failure = null;
        for (var attempt = 0; attempt < 240; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            using var client = OpenAiProviderUtility.CreateClient(_apiKey!);
            using var response = await client.GetAsync($"{_baseUrl}/videos/{Uri.EscapeDataString(videoId)}", cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            OpenAiProviderUtility.EnsureSuccess(response, body, DisplayName);
            using var document = JsonDocument.Parse(body);
            var status = document.RootElement.TryGetProperty("status", out var statusElement)
                ? statusElement.GetString()
                : null;
            if (string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
            {
                completed = true;
                break;
            }
            if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase))
            {
                failure = OpenAiProviderUtility.ReadVideoError(document.RootElement) ?? $"สถานะ {status}";
                break;
            }
        }
        if (!completed)
            throw new InvalidOperationException(failure ?? "OpenAI Video ใช้เวลานานเกินกำหนดหรือยังไม่เสร็จ");

        var output = Path.GetFullPath(request.OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using (var client = OpenAiProviderUtility.CreateClient(_apiKey!))
        using (var response = await client.GetAsync(
                   $"{_baseUrl}/videos/{Uri.EscapeDataString(videoId)}/content",
                   HttpCompletionOption.ResponseHeadersRead,
                   cancellationToken))
        {
            var errorBody = response.IsSuccessStatusCode
                ? string.Empty
                : await response.Content.ReadAsStringAsync(cancellationToken);
            OpenAiProviderUtility.EnsureSuccess(response, errorBody, DisplayName);
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var file = File.Create(output);
            await input.CopyToAsync(file, cancellationToken);
        }
        if (!File.Exists(output) || new FileInfo(output).Length < 4096)
            throw new InvalidDataException("ไฟล์วิดีโอที่สร้างไม่สมบูรณ์");

        return new GeneratedMediaAsset
        {
            ProviderId = ProviderId,
            Kind = "video",
            Path = output,
            Prompt = request.Prompt,
            Model = string.IsNullOrWhiteSpace(request.Model) ? "sora-2" : request.Model
        };
    }
}

internal static class OpenAiProviderUtility
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string? ResolveApiKey(string? explicitValue) =>
        string.IsNullOrWhiteSpace(explicitValue)
            ? Environment.GetEnvironmentVariable("OPENAI_API_KEY")?.Trim()
            : explicitValue.Trim();

    public static string ResolveBaseUrl(string? explicitValue)
    {
        var value = string.IsNullOrWhiteSpace(explicitValue)
            ? Environment.GetEnvironmentVariable("OPENAI_BASE_URL")
            : explicitValue;
        return string.IsNullOrWhiteSpace(value)
            ? "https://api.openai.com/v1"
            : value.Trim().TrimEnd('/');
    }

    public static ProviderAvailability Availability(string providerId, string displayName, string? apiKey) => new()
    {
        ProviderId = providerId,
        DisplayName = displayName,
        IsReady = !string.IsNullOrWhiteSpace(apiKey),
        Status = string.IsNullOrWhiteSpace(apiKey) ? "missing_api_key" : "ready",
        Message = string.IsNullOrWhiteSpace(apiKey)
            ? "ยังไม่พบ OPENAI_API_KEY หรือ API key ชั่วคราวในหน้าต่าง Script to Video"
            : "พบ API key แล้ว พร้อมส่งคำขอเมื่อผู้ใช้กด Generate"
    };

    public static HttpClient CreateClient(string apiKey)
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(25)
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }

    public static void ValidatePromptAndOutput(string value, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("ข้อความหรือ Prompt ว่างเปล่า");
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("ไม่ได้ระบุ Output path");
    }

    public static void EnsureApiKey(string? apiKey, string displayName)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"{displayName} ต้องใช้ OPENAI_API_KEY หรือ API key ชั่วคราวในหน้าต่าง Script to Video");
    }

    public static void EnsureSuccess(HttpResponseMessage response, string body, string displayName)
    {
        if (response.IsSuccessStatusCode)
            return;
        var message = ReadApiError(body) ?? body.Trim();
        if (message.Length > 1000)
            message = message[..1000];
        throw new HttpRequestException($"{displayName} ตอบกลับ {(int)response.StatusCode}: {message}");
    }

    public static string? ReadApiError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message))
                    return message.GetString();
                if (error.ValueKind == JsonValueKind.String)
                    return error.GetString();
            }
        }
        catch
        {
        }
        return null;
    }

    public static string? ReadVideoError(JsonElement root)
    {
        if (!root.TryGetProperty("error", out var error) || error.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message))
            return message.GetString();
        return error.ToString();
    }

    public static string ImageMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "image/jpeg"
    };
}
