using AutoCutStudio.Core.Interfaces;

namespace AutoCutStudio.Infrastructure;

public sealed class ToolLocator : IToolLocator
{
    public ToolAvailability Locate()
    {
        var ffmpeg = LocateExecutable("AUTOCUT_FFMPEG_PATH", "ffmpeg");
        var ffprobe = LocateExecutable("AUTOCUT_FFPROBE_PATH", "ffprobe");

        if (ffmpeg is null || ffprobe is null)
        {
            var missing = new List<string>();
            if (ffmpeg is null)
            {
                missing.Add("ffmpeg");
            }

            if (ffprobe is null)
            {
                missing.Add("ffprobe");
            }

            return new ToolAvailability(
                false,
                ffmpeg,
                ffprobe,
                "missing",
                $"ต้องติดตั้ง Dependency เพิ่ม: {string.Join(", ", missing)}");
        }

        return new ToolAvailability(true, ffmpeg, ffprobe, "ready", "FFmpeg และ FFprobe พร้อมใช้งาน");
    }

    private static string? LocateExecutable(string environmentVariable, string baseName)
    {
        var extension = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        var explicitPath = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", baseName + extension),
            Path.Combine(AppContext.BaseDirectory, baseName + extension)
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), baseName + extension);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch
            {
                // Ignore malformed PATH entries and continue.
            }
        }

        return null;
    }
}
