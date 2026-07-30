using System.Diagnostics;
using System.Text;

namespace AutoCutStudio.Infrastructure;

public sealed class WindowsVoiceoverService
{
    public async Task GenerateAsync(
        string text,
        string outputWavePath,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows Voiceover ใช้ได้เฉพาะ Windows");
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Voiceover text is empty.", nameof(text));
        Directory.CreateDirectory(Path.GetDirectoryName(outputWavePath)!);
        var escapedPath = outputWavePath.Replace("'", "''", StringComparison.Ordinal);
        var escapedText = text.Replace("'", "''", StringComparison.Ordinal);
        var script = $$"""
            Add-Type -AssemblyName System.Speech
            $voice = New-Object System.Speech.Synthesis.SpeechSynthesizer
            try {
                $voice.Rate = 0
                $voice.Volume = 100
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
        foreach (var argument in new[]
        {
            "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded
        }) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("ไม่สามารถเริ่ม Windows Voiceover ได้");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try { await process.WaitForExitAsync(cancellationToken); }
        catch
        {
            if (!process.HasExited) process.Kill(true);
            throw;
        }
        _ = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0 || !File.Exists(outputWavePath) || new FileInfo(outputWavePath).Length < 1000)
            throw new InvalidDataException($"Windows Voiceover ไม่สำเร็จ: {error}");
    }
}
