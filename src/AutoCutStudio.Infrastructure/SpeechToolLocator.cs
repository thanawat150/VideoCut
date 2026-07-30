using AutoCutStudio.Core.Models;

namespace AutoCutStudio.Infrastructure;

public sealed class SpeechToolLocator
{
    public SpeechToolAvailability Locate()
    {
        var extension = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        var whisper = LocateFile(
            "AUTOCUT_WHISPER_CLI_PATH",
            [
                Path.Combine(AppContext.BaseDirectory, "tools", "whisper", "whisper-cli" + extension),
                Path.Combine(AppContext.BaseDirectory, "whisper-cli" + extension)
            ]);
        var model = LocateFile(
            "AUTOCUT_WHISPER_MODEL_PATH",
            [
                Path.Combine(AppContext.BaseDirectory, "models", "whisper", "ggml-base-q5_1.bin"),
                Path.Combine(AppContext.BaseDirectory, "models", "whisper", "ggml-base.bin")
            ]);

        if (whisper is null || model is null)
        {
            var missing = new List<string>();
            if (whisper is null)
            {
                missing.Add("whisper-cli");
            }

            if (model is null)
            {
                missing.Add("Whisper model");
            }

            return new SpeechToolAvailability(
                false,
                whisper,
                model,
                "missing",
                $"ต้องติดตั้ง Dependency ถอดเสียงเพิ่ม: {string.Join(", ", missing)}");
        }

        return new SpeechToolAvailability(
            true,
            whisper,
            model,
            "ready",
            "Whisper Local Speech-to-Text พร้อมใช้งาน");
    }

    private static string? LocateFile(string environmentVariable, IReadOnlyList<string> candidates)
    {
        var explicitPath = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }
}
