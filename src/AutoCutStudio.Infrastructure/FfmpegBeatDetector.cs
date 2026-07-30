using System.Diagnostics;
using System.Runtime.InteropServices;
using AutoCutStudio.Core.Models;

namespace AutoCutStudio.Infrastructure;

public sealed class FfmpegBeatDetector
{
    private readonly ToolLocator _toolLocator;

    public FfmpegBeatDetector(ToolLocator toolLocator) => _toolLocator = toolLocator;

    public async Task<BeatAnalysisResult> AnalyzeAsync(
        string inputPath,
        double durationSeconds,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException("ไม่พบไฟล์เสียงหรือวิดีโอสำหรับวิเคราะห์ Beat", inputPath);
        var tools = _toolLocator.Locate();
        if (!tools.IsReady || string.IsNullOrWhiteSpace(tools.FfmpegPath))
            throw new InvalidOperationException(tools.Message);

        const int sampleRate = 8000;
        const int samplesPerWindow = 400; // 50 ms
        var startInfo = new ProcessStartInfo
        {
            FileName = tools.FfmpegPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
        {
            "-hide_banner", "-nostdin", "-i", inputPath,
            "-vn", "-ac", "1", "-ar", sampleRate.ToString(),
            "-f", "f32le", "pipe:1"
        }) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("ไม่สามารถเริ่ม FFmpeg Beat analysis ได้");
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var energy = new List<double>();
        var byteBuffer = new byte[samplesPerWindow * sizeof(float)];
        try
        {
            while (true)
            {
                var offset = 0;
                while (offset < byteBuffer.Length)
                {
                    var read = await process.StandardOutput.BaseStream.ReadAsync(
                        byteBuffer.AsMemory(offset, byteBuffer.Length - offset), cancellationToken);
                    if (read == 0) break;
                    offset += read;
                }
                if (offset == 0) break;
                var floats = MemoryMarshal.Cast<byte, float>(byteBuffer.AsSpan(0, offset - offset % 4));
                double sum = 0;
                foreach (var sample in floats)
                {
                    var bounded = float.IsFinite(sample) ? sample : 0;
                    sum += bounded * bounded;
                }
                energy.Add(floats.Length == 0 ? 0 : Math.Sqrt(sum / floats.Length));
                if (offset < byteBuffer.Length) break;
            }
            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
            if (!process.HasExited) process.Kill(true);
            throw;
        }

        var error = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidDataException($"FFmpeg Beat analysis ล้มเหลว: {Tail(error, 12)}");
        if (energy.Count < 10)
            return new BeatAnalysisResult { InputPath = inputPath, DurationSeconds = durationSeconds };

        var novelty = new double[energy.Count];
        for (var index = 1; index < energy.Count; index++)
            novelty[index] = Math.Max(0, energy[index] - energy[index - 1]);
        var mean = novelty.Average();
        var std = Math.Sqrt(novelty.Select(value => Math.Pow(value - mean, 2)).Average());
        // A lower adaptive multiplier preserves regular click tracks where roughly half of the
        // windows are quiet and the positive onsets are intentionally uniform.
        var threshold = mean + std * 0.45;
        var minimumGapWindows = 5; // 250 ms
        var peaks = new List<int>();
        for (var index = 1; index < novelty.Length - 1; index++)
        {
            if (novelty[index] < threshold || novelty[index] < novelty[index - 1] || novelty[index] < novelty[index + 1])
                continue;
            if (peaks.Count > 0 && index - peaks[^1] < minimumGapWindows)
            {
                if (novelty[index] > novelty[peaks[^1]]) peaks[^1] = index;
                continue;
            }
            peaks.Add(index);
        }

        var markers = peaks.Select((window, index) => new BeatMarker
        {
            Index = index + 1,
            TimeSeconds = window * samplesPerWindow / (double)sampleRate,
            Strength = threshold <= 0 ? 0 : Math.Clamp(novelty[window] / threshold, 0, 5)
        }).ToList();
        var intervals = markers.Zip(markers.Skip(1), (left, right) => right.TimeSeconds - left.TimeSeconds)
            .Where(value => value is >= 0.25 and <= 2.0)
            .ToList();
        var bpm = intervals.Count == 0 ? 0 : 60 / Median(intervals);
        while (bpm > 180) bpm /= 2;
        while (bpm is > 0 and < 60) bpm *= 2;

        return new BeatAnalysisResult
        {
            InputPath = inputPath,
            DurationSeconds = durationSeconds,
            EstimatedBpm = bpm,
            Beats = markers
        };
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        var middle = values.Count / 2;
        return values.Count % 2 == 0 ? (values[middle - 1] + values[middle]) / 2 : values[middle];
    }

    private static string Tail(string value, int count) =>
        string.Join(Environment.NewLine, value.Split(['\r','\n'], StringSplitOptions.RemoveEmptyEntries).TakeLast(count));
}
