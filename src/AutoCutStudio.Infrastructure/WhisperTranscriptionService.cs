using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;

namespace AutoCutStudio.Infrastructure;

public sealed partial class WhisperTranscriptionService
{
    private readonly ToolLocator _mediaTools;
    private readonly SpeechToolLocator _speechTools;

    public WhisperTranscriptionService(ToolLocator mediaTools, SpeechToolLocator speechTools)
    {
        _mediaTools = mediaTools;
        _speechTools = speechTools;
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        string inputPath,
        string projectRoot,
        string workingRoot,
        Guid projectId,
        Guid mediaAssetId,
        TranscriptionOptions options,
        Func<double?, string, Task>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("ไม่พบ Source Media สำหรับถอดเสียง", inputPath);
        }

        var media = _mediaTools.Locate();
        if (!media.IsReady || string.IsNullOrWhiteSpace(media.FfmpegPath))
        {
            throw new InvalidOperationException(media.Message);
        }

        var speech = _speechTools.Locate();
        if (!speech.IsReady ||
            string.IsNullOrWhiteSpace(speech.WhisperCliPath) ||
            string.IsNullOrWhiteSpace(speech.ModelPath))
        {
            throw new InvalidOperationException(speech.Message);
        }

        var runId = Guid.NewGuid().ToString("N");
        var runDirectory = Path.Combine(workingRoot, "Speech", runId);
        Directory.CreateDirectory(runDirectory);
        var wavPath = Path.Combine(runDirectory, "audio.wav");
        var outputBase = Path.Combine(runDirectory, "transcript");
        var stagedModel = await EnsureAsciiArgumentPathAsync(
            speech.ModelPath,
            Path.Combine(workingRoot, "SpeechModels"),
            cancellationToken);

        await ReportAsync(progress, 0, "กำลังแยกเสียง 16 kHz mono ด้วย FFmpeg");
        await ExtractAudioAsync(media.FfmpegPath, inputPath, wavPath, cancellationToken);
        await ReportAsync(progress, 10, "แยกเสียงแล้ว กำลังเริ่ม Whisper Local");

        var log = new List<string>();
        var startInfo = new ProcessStartInfo
        {
            FileName = speech.WhisperCliPath,
            WorkingDirectory = Path.GetDirectoryName(speech.WhisperCliPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in BuildArguments(stagedModel, wavPath, outputBase, options))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("ไม่สามารถเริ่ม Whisper Local ได้");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        try
        {
            while (await process.StandardError.ReadLineAsync(cancellationToken) is { } line)
            {
                log.Add(line);
                if (log.Count > 500)
                {
                    log.RemoveRange(0, 100);
                }

                var match = ProgressRegex().Match(line);
                if (match.Success &&
                    double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    await ReportAsync(
                        progress,
                        10 + Math.Clamp(value, 0, 100) * 0.85,
                        $"Whisper กำลังถอดเสียง {value:0}%");
                }
            }

            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        var stdout = await stdoutTask;
        if (!string.IsNullOrWhiteSpace(stdout))
        {
            log.AddRange(stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).TakeLast(100));
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidDataException(
                $"Whisper ถอดเสียงไม่สำเร็จ (exit {process.ExitCode})\n{Tail(log, 25)}");
        }

        var jsonPath = outputBase + ".json";
        var srtPath = outputBase + ".srt";
        var textPath = outputBase + ".txt";
        if (!File.Exists(jsonPath))
        {
            throw new InvalidDataException("Whisper ทำงานจบแต่ไม่พบไฟล์ JSON Transcript");
        }

        var transcript = await ParseTranscriptAsync(
            jsonPath,
            projectId,
            mediaAssetId,
            inputPath,
            options.Language,
            Path.GetFileName(speech.ModelPath),
            cancellationToken);

        var transcriptDirectory = Path.Combine(projectRoot, "transcript");
        var subtitleDirectory = Path.Combine(projectRoot, "subtitles");
        Directory.CreateDirectory(transcriptDirectory);
        Directory.CreateDirectory(subtitleDirectory);

        var finalJson = Path.Combine(transcriptDirectory, "whisper-result.json");
        var finalTranscript = Path.Combine(transcriptDirectory, "transcript.json");
        var finalText = Path.Combine(transcriptDirectory, "transcript.txt");
        var finalSrt = Path.Combine(subtitleDirectory, "transcript.srt");
        File.Copy(jsonPath, finalJson, true);
        if (File.Exists(textPath))
        {
            File.Copy(textPath, finalText, true);
        }
        else
        {
            await File.WriteAllTextAsync(finalText, transcript.PlainText, new UTF8Encoding(false), cancellationToken);
        }

        if (File.Exists(srtPath))
        {
            File.Copy(srtPath, finalSrt, true);
        }

        await File.WriteAllTextAsync(
            finalTranscript,
            JsonSerializer.Serialize(transcript, JsonDefaults.Options),
            new UTF8Encoding(false),
            cancellationToken);
        await ReportAsync(progress, 100, $"ถอดเสียงสำเร็จ {transcript.Segments.Count} ช่วง");

        TryDeleteDirectory(runDirectory);
        return new TranscriptionResult
        {
            Transcript = transcript,
            JsonPath = finalJson,
            SrtPath = finalSrt,
            TextPath = finalText,
            AudioPath = string.Empty,
            SafeWhisperCommand =
                $"whisper-cli -m <local-model> -f <16k-mono-wav> -l {options.Language} -ojf -osrt -otxt -of <project-transcript>",
            TechnicalLogTail = Tail(log, 35)
        };
    }

    private static IReadOnlyList<string> BuildArguments(
        string modelPath,
        string wavPath,
        string outputBase,
        TranscriptionOptions options)
    {
        var arguments = new List<string>
        {
            "-m", modelPath,
            "-f", wavPath,
            "-l", string.IsNullOrWhiteSpace(options.Language) ? "auto" : options.Language,
            "-t", Math.Clamp(options.Threads, 1, 32).ToString(CultureInfo.InvariantCulture),
            "-ojf",
            "-osrt",
            "-otxt",
            "-pp",
            "-of", outputBase
        };

        if (!options.UseGpu)
        {
            arguments.Add("-ng");
        }

        if (!string.IsNullOrWhiteSpace(options.InitialPrompt))
        {
            arguments.Add("--prompt");
            arguments.Add(options.InitialPrompt.Trim());
        }

        return arguments;
    }

    private static async Task ExtractAudioAsync(
        string ffmpegPath,
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in new[]
        {
            "-hide_banner", "-nostdin", "-y",
            "-i", inputPath,
            "-vn",
            "-ar", "16000",
            "-ac", "1",
            "-c:a", "pcm_s16le",
            outputPath
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("ไม่สามารถเริ่ม FFmpeg เพื่อแยกเสียงได้");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        _ = await stdoutTask;
        var error = await stderrTask;
        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            throw new InvalidDataException($"FFmpeg แยกเสียงไม่สำเร็จ\n{Tail(error.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries), 18)}");
        }
    }

    private static async Task<TranscriptDocument> ParseTranscriptAsync(
        string jsonPath,
        Guid projectId,
        Guid mediaAssetId,
        string sourcePath,
        string requestedLanguage,
        string modelName,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(jsonPath);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var detectedLanguage = root.TryGetProperty("result", out var result) &&
                               result.TryGetProperty("language", out var languageElement)
            ? languageElement.GetString() ?? string.Empty
            : string.Empty;
        var segments = new List<TranscriptSegment>();

        if (root.TryGetProperty("transcription", out var transcription) &&
            transcription.ValueKind == JsonValueKind.Array)
        {
            var sequence = 1;
            foreach (var item in transcription.EnumerateArray())
            {
                var text = item.TryGetProperty("text", out var textElement)
                    ? (textElement.GetString() ?? string.Empty).Trim()
                    : string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var start = 0d;
                var end = 0d;
                if (item.TryGetProperty("offsets", out var offsets))
                {
                    if (offsets.TryGetProperty("from", out var fromElement))
                    {
                        start = fromElement.GetDouble() / 1000d;
                    }

                    if (offsets.TryGetProperty("to", out var toElement))
                    {
                        end = toElement.GetDouble() / 1000d;
                    }
                }

                var probabilities = new List<double>();
                if (item.TryGetProperty("tokens", out var tokens) && tokens.ValueKind == JsonValueKind.Array)
                {
                    foreach (var token in tokens.EnumerateArray())
                    {
                        if (token.TryGetProperty("p", out var probability) && probability.TryGetDouble(out var value))
                        {
                            probabilities.Add(value);
                        }
                    }
                }

                segments.Add(new TranscriptSegment
                {
                    Sequence = sequence++,
                    StartSeconds = Math.Max(0, start),
                    EndSeconds = Math.Max(start, end),
                    Text = text,
                    OriginalText = text,
                    AverageProbability = probabilities.Count == 0 ? null : probabilities.Average()
                });
            }
        }

        if (segments.Count == 0)
        {
            throw new InvalidDataException("Whisper ไม่คืนข้อความ Transcript ที่ใช้งานได้");
        }

        return new TranscriptDocument
        {
            ProjectId = projectId,
            MediaAssetId = mediaAssetId,
            SourcePath = sourcePath,
            Language = requestedLanguage,
            DetectedLanguage = detectedLanguage,
            ModelName = modelName,
            Segments = segments
        };
    }

    private static async Task<string> EnsureAsciiArgumentPathAsync(
        string sourcePath,
        string cacheDirectory,
        CancellationToken cancellationToken)
    {
        if (sourcePath.All(character => character <= 127))
        {
            return sourcePath;
        }

        Directory.CreateDirectory(cacheDirectory);
        var target = Path.Combine(cacheDirectory, Path.GetFileName(sourcePath));
        if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(sourcePath).Length)
        {
            await using var input = File.OpenRead(sourcePath);
            await using var output = File.Create(target);
            await input.CopyToAsync(output, cancellationToken);
        }

        return target;
    }

    private static Task ReportAsync(
        Func<double?, string, Task>? progress,
        double? value,
        string message) => progress?.Invoke(value, message) ?? Task.CompletedTask;

    private static string Tail(IEnumerable<string> lines, int count) =>
        string.Join(Environment.NewLine, lines.TakeLast(count));

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort cancellation.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // Cache cleanup is best effort and never invalidates a completed transcript.
        }
    }

    [GeneratedRegex(@"progress\s*=\s*(\d+(?:\.\d+)?)%", RegexOptions.IgnoreCase)]
    private static partial Regex ProgressRegex();
}
