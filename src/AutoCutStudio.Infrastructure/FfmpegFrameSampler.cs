using System.Diagnostics;
using System.Globalization;

namespace AutoCutStudio.Infrastructure;

public sealed record SampledFrame(string Path, int Index, double TimeSeconds);

public sealed class FfmpegFrameSampler
{
    private readonly ToolLocator _tools;

    public FfmpegFrameSampler(ToolLocator tools) => _tools = tools;

    public async Task<IReadOnlyList<SampledFrame>> ExtractAsync(
        string inputPath,
        string outputDirectory,
        double sampleIntervalSeconds,
        int maximumFrames = 240,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException("ไม่พบวิดีโอสำหรับสุ่ม Frame", inputPath);
        var availability = _tools.Locate();
        if (!availability.IsReady || string.IsNullOrWhiteSpace(availability.FfmpegPath))
            throw new InvalidOperationException(availability.Message);
        sampleIntervalSeconds = Math.Clamp(sampleIntervalSeconds, 0.1, 10);
        maximumFrames = Math.Clamp(maximumFrames, 1, 2_000);
        Directory.CreateDirectory(outputDirectory);
        foreach (var oldFile in Directory.EnumerateFiles(outputDirectory, "frame-*.jpg"))
            File.Delete(oldFile);

        var outputPattern = Path.Combine(outputDirectory, "frame-%06d.jpg");
        var fpsExpression = (1d / sampleIntervalSeconds).ToString("0.########", CultureInfo.InvariantCulture);
        var startInfo = new ProcessStartInfo
        {
            FileName = availability.FfmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[]
        {
            "-hide_banner", "-nostdin", "-y",
            "-i", inputPath,
            "-vf", $"fps={fpsExpression}",
            "-frames:v", maximumFrames.ToString(CultureInfo.InvariantCulture),
            "-q:v", "2",
            outputPattern
        }) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("ไม่สามารถเริ่ม FFmpeg Frame Sampler ได้");
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
        if (process.ExitCode != 0)
            throw new InvalidDataException($"FFmpeg Frame Sampler ล้มเหลว: {Tail(error, 15)}");

        return Directory.EnumerateFiles(outputDirectory, "frame-*.jpg")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select((path, index) => new SampledFrame(path, index + 1, index * sampleIntervalSeconds))
            .ToList();
    }

    private static string Tail(string value, int count) =>
        string.Join(Environment.NewLine, value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).TakeLast(count));
}
