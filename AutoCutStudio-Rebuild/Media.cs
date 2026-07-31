using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace AutoCutStudio.Rebuild;

public sealed class SafeProcess
{
    public async Task<int> RunAsync(string executable, IReadOnlyList<string> arguments, Action<string>? output = null, Action<string>? error = null, CancellationToken cancellationToken = default)
    {
        var info = new ProcessStartInfo { FileName = executable, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = info };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output?.Invoke(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) error?.Invoke(e.Data); };
        if (!process.Start()) throw new InvalidOperationException($"Unable to start {executable}.");
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }
}

public static class Tools
{
    public static string Find(string name)
    {
        var variable = name switch { "ffmpeg" => "AUTOCUT_REBUILD_FFMPEG", "ffprobe" => "AUTOCUT_REBUILD_FFPROBE", _ => "AUTOCUT_REBUILD_WHISPER" };
        var configured = Environment.GetEnvironmentVariable(variable);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured!;
        var executable = OperatingSystem.IsWindows() ? name + ".exe" : name;
        var folder = name.StartsWith("ff", StringComparison.OrdinalIgnoreCase) ? "ffmpeg" : "whisper";
        var bundled = Path.Combine(AppContext.BaseDirectory, "tools", folder, executable);
        return File.Exists(bundled) ? bundled : executable;
    }
}

public sealed class FfprobeService(SafeProcess runner)
{
    public async Task<MediaProbe> ProbeAsync(string path, CancellationToken cancellationToken = default)
    {
        path = Security.ExistingFile(path);
        var lines = new List<string>(); var errors = new List<string>();
        var exit = await runner.RunAsync(Tools.Find("ffprobe"), ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", path], lines.Add, errors.Add, cancellationToken);
        if (exit != 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        using var json = JsonDocument.Parse(string.Join(Environment.NewLine, lines));
        var root = json.RootElement;
        var streams = root.GetProperty("streams").EnumerateArray().ToArray();
        var video = streams.FirstOrDefault(IsVideo); var audio = streams.FirstOrDefault(IsAudio); var format = root.GetProperty("format");
        var r = Get(video, "r_frame_rate"); var avg = Get(video, "avg_frame_rate");
        return new MediaProbe(Get(format, "format_name") ?? "", Get(video, "codec_name") ?? "", Get(audio, "codec_name"), Int(video, "width"), Int(video, "height"), Rate(avg) > 0 ? Rate(avg) : Rate(r), Double(format, "duration"), video.ValueKind != JsonValueKind.Undefined, audio.ValueKind != JsonValueKind.Undefined, long.TryParse(Get(format, "size"), out var size) ? size : new FileInfo(path).Length, !string.Equals(r, avg, StringComparison.Ordinal));
    }

    private static bool IsVideo(JsonElement e) => Get(e, "codec_type") == "video";
    private static bool IsAudio(JsonElement e) => Get(e, "codec_type") == "audio";
    private static string? Get(JsonElement e, string name) => e.ValueKind != JsonValueKind.Undefined && e.TryGetProperty(name, out var value) ? value.ToString() : null;
    private static int Int(JsonElement e, string name) => int.TryParse(Get(e, name), out var value) ? value : 0;
    private static double Double(JsonElement e, string name) => double.TryParse(Get(e, name), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0;
    private static double Rate(string? value) { if (string.IsNullOrWhiteSpace(value)) return 0; var p = value.Split('/'); return p.Length == 2 && double.TryParse(p[0], out var n) && double.TryParse(p[1], out var d) && d != 0 ? n / d : double.TryParse(value, out var x) ? x : 0; }
}

