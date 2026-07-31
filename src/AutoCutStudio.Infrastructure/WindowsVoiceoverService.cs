using System.Diagnostics;
using System.Text;

namespace AutoCutStudio.Infrastructure;

public sealed record WindowsVoiceoverOptions
{
    public int Rate { get; init; }
    public int Volume { get; init; } = 100;
    public string Language { get; init; } = "auto";
    public string? PreferredVoiceName { get; init; }
}

public sealed class WindowsVoiceoverService
{
    public async Task GenerateAsync(
        string text,
        string outputWavePath,
        CancellationToken cancellationToken = default) =>
        await GenerateAsync(text, outputWavePath, new WindowsVoiceoverOptions(), cancellationToken);

    public async Task GenerateAsync(
        string text,
        string outputWavePath,
        WindowsVoiceoverOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows Voiceover ใช้ได้เฉพาะ Windows");
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Voiceover text is empty.", nameof(text));

        var outputFullPath = Path.GetFullPath(outputWavePath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputFullPath)!);
        var textPath = outputFullPath + ".narration.txt";
        await File.WriteAllTextAsync(textPath, text, new UTF8Encoding(false), cancellationToken);

        var escapedOutput = EscapePowerShellLiteral(outputFullPath);
        var escapedTextPath = EscapePowerShellLiteral(textPath);
        var preferredVoice = EscapePowerShellLiteral(options.PreferredVoiceName ?? string.Empty);
        var language = options.Language.Trim().ToLowerInvariant();
        var culturePrefix = language is "" or "auto"
            ? string.Empty
            : language.Split('-', StringSplitOptions.RemoveEmptyEntries)[0];
        var escapedCulture = EscapePowerShellLiteral(culturePrefix);
        var rate = Math.Clamp(options.Rate, -10, 10);
        var volume = Math.Clamp(options.Volume, 0, 100);

        var script = $$"""
            Add-Type -AssemblyName System.Speech
            $voice = New-Object System.Speech.Synthesis.SpeechSynthesizer
            try {
                $preferred = '{{preferredVoice}}'
                $culturePrefix = '{{escapedCulture}}'
                if (-not [string]::IsNullOrWhiteSpace($preferred)) {
                    $match = $voice.GetInstalledVoices() |
                        Where-Object { $_.Enabled -and $_.VoiceInfo.Name -eq $preferred } |
                        Select-Object -First 1
                    if ($null -eq $match) {
                        throw "ไม่พบ Windows voice ชื่อ: $preferred"
                    }
                    $voice.SelectVoice($match.VoiceInfo.Name)
                }
                elseif (-not [string]::IsNullOrWhiteSpace($culturePrefix)) {
                    $match = $voice.GetInstalledVoices() |
                        Where-Object { $_.Enabled -and $_.VoiceInfo.Culture.Name.ToLowerInvariant().StartsWith($culturePrefix) } |
                        Select-Object -First 1
                    if ($null -eq $match) {
                        $installed = ($voice.GetInstalledVoices() |
                            Where-Object { $_.Enabled } |
                            ForEach-Object { $_.VoiceInfo.Culture.Name + ':' + $_.VoiceInfo.Name }) -join ', '
                        throw "ไม่พบ Windows voice สำหรับภาษา $culturePrefix | Installed: $installed"
                    }
                    $voice.SelectVoice($match.VoiceInfo.Name)
                }
                $voice.Rate = {{rate}}
                $voice.Volume = {{volume}}
                $text = [System.IO.File]::ReadAllText('{{escapedTextPath}}', [System.Text.Encoding]::UTF8)
                $voice.SetOutputToWaveFile('{{escapedOutput}}')
                $voice.Speak($text)
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
        foreach (var argument in new[]
        {
            "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo)
                                ?? throw new InvalidOperationException("ไม่สามารถเริ่ม Windows Voiceover ได้");
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
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
            _ = await stdout;
            var error = await stderr;
            if (process.ExitCode != 0 || !File.Exists(outputFullPath) || new FileInfo(outputFullPath).Length < 1000)
                throw new InvalidDataException($"Windows Voiceover ไม่สำเร็จ: {error}");
        }
        finally
        {
            try
            {
                if (File.Exists(textPath))
                    File.Delete(textPath);
            }
            catch
            {
                // The narration file contains only the supplied script and is removed best-effort.
            }
        }
    }

    private static string EscapePowerShellLiteral(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);
}
