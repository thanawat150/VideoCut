using System.Diagnostics;
using System.Text;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Infrastructure;

namespace AutoCutStudio.Tests;

public sealed class WhisperIntegrationTests
{
    [Fact]
    public async Task RealWhisperCliCreatesTranscriptAndSrtWithoutExposingPaths()
    {
        var mediaTools = new ToolLocator();
        var mediaAvailability = mediaTools.Locate();
        Assert.True(mediaAvailability.IsReady, mediaAvailability.Message);

        var speechTools = new SpeechToolLocator();
        var speechAvailability = speechTools.Locate();
        Assert.True(speechAvailability.IsReady, speechAvailability.Message);

        var root = Path.Combine(
            Path.GetTempPath(),
            "AutoCut Whisper ภาษาไทย " + Guid.NewGuid().ToString("N"));
        var projectRoot = Path.Combine(root, "Project มีช่องว่าง");
        var cacheRoot = Path.Combine(root, "Cache");
        Directory.CreateDirectory(projectRoot);
        Directory.CreateDirectory(cacheRoot);

        try
        {
            var speechWav = Path.Combine(root, "generated speech.wav");
            await GenerateSpeechWithWindowsSapiAsync(
                speechWav,
                "Hello. This is a local automatic video transcription test.");
            Assert.True(File.Exists(speechWav));

            var service = new WhisperTranscriptionService(mediaTools, speechTools);
            var progressEvents = new List<(double? Value, string Message)>();
            var result = await service.TranscribeAsync(
                speechWav,
                projectRoot,
                cacheRoot,
                Guid.NewGuid(),
                Guid.NewGuid(),
                new TranscriptionOptions
                {
                    Language = "en",
                    Threads = 2,
                    UseGpu = false
                },
                (value, message) =>
                {
                    progressEvents.Add((value, message));
                    return Task.CompletedTask;
                });

            Assert.NotEmpty(result.Transcript.Segments);
            Assert.False(string.IsNullOrWhiteSpace(result.Transcript.PlainText));
            Assert.True(File.Exists(result.JsonPath));
            Assert.True(File.Exists(result.SrtPath));
            Assert.True(File.Exists(result.TextPath));
            Assert.Contains(progressEvents, item => item.Value == 100);
            Assert.DoesNotContain(speechWav, result.SafeWhisperCommand, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                speechAvailability.ModelPath!,
                result.SafeWhisperCommand,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains("whisper-cli", result.SafeWhisperCommand);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static async Task GenerateSpeechWithWindowsSapiAsync(string outputPath, string text)
    {
        var escapedPath = outputPath.Replace("'", "''", StringComparison.Ordinal);
        var escapedText = text.Replace("'", "''", StringComparison.Ordinal);
        var script = $$"""
            Add-Type -AssemblyName System.Speech
            $voice = New-Object System.Speech.Synthesis.SpeechSynthesizer
            try {
                $voice.SetOutputToWaveFile('{{escapedPath}}')
                $voice.Speak('{{escapedText}}')
            }
            finally {
                $voice.Dispose()
            }
            """;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-EncodedCommand");
        startInfo.ArgumentList.Add(encoded);

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Unable to start Windows SAPI test generator.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        Assert.True(
            process.ExitCode == 0,
            $"Windows SAPI failed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        Assert.True(File.Exists(outputPath), "Windows SAPI did not create a WAV file.");
        Assert.True(new FileInfo(outputPath).Length > 1_000, "Generated WAV is unexpectedly small.");
    }
}
