using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AutoCutStudio.Core.Models;

namespace AutoCutStudio.Infrastructure;

public sealed partial class FfmpegSilenceDetector
{
    private readonly ToolLocator _toolLocator;

    public FfmpegSilenceDetector(ToolLocator toolLocator)
    {
        _toolLocator = toolLocator;
    }

    public async Task<SilenceDetectionResult> DetectAsync(
        string inputPath,
        double sourceDurationSeconds,
        SilenceDetectionOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("ไม่พบไฟล์วิดีโอสำหรับตรวจช่วงเงียบ", inputPath);
        }

        var tools = _toolLocator.Locate();
        if (!tools.IsReady || string.IsNullOrWhiteSpace(tools.FfmpegPath))
        {
            throw new InvalidOperationException(tools.Message);
        }

        var filter = FormattableString.Invariant(
            $"silencedetect=n={options.NoiseThresholdDb:0.###}dB:d={options.MinimumSilenceSeconds:0.###}");
        var startInfo = new ProcessStartInfo
        {
            FileName = tools.FfmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in new[]
        {
            "-hide_banner",
            "-nostdin",
            "-i", inputPath,
            "-map", "0:a:0",
            "-af", filter,
            "-f", "null",
            "-"
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("ไม่สามารถเริ่ม FFmpeg สำหรับตรวจช่วงเงียบได้");
        }

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

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidDataException(
                $"FFmpeg ตรวจช่วงเงียบไม่สำเร็จ (exit {process.ExitCode})\n{Tail(stderr, 12)}");
        }

        var intervals = ParseIntervals(stderr, sourceDurationSeconds);
        return new SilenceDetectionResult
        {
            InputPath = inputPath,
            SourceDurationSeconds = sourceDurationSeconds,
            Options = options,
            Intervals = intervals,
            SafeCommandDisplay =
                $"ffmpeg -hide_banner -nostdin -i <source> -map 0:a:0 -af {filter} -f null -",
            TechnicalLogTail = Tail(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr, 30),
            CompletedAt = DateTimeOffset.UtcNow
        };
    }

    internal static IReadOnlyList<SilenceInterval> ParseIntervals(
        string log,
        double sourceDurationSeconds)
    {
        var intervals = new List<SilenceInterval>();
        double? openStart = null;

        foreach (var rawLine in log.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var startMatch = SilenceStartRegex().Match(rawLine);
            if (startMatch.Success && TryParse(startMatch.Groups[1].Value, out var start))
            {
                openStart = Math.Clamp(start, 0, sourceDurationSeconds);
            }

            var endMatch = SilenceEndRegex().Match(rawLine);
            if (!endMatch.Success || !TryParse(endMatch.Groups[1].Value, out var end))
            {
                continue;
            }

            var boundedEnd = Math.Clamp(end, 0, sourceDurationSeconds);
            var startValue = openStart;
            if (startValue is null)
            {
                var durationMatch = SilenceDurationRegex().Match(rawLine);
                if (durationMatch.Success && TryParse(durationMatch.Groups[1].Value, out var silenceDuration))
                {
                    startValue = Math.Max(0, boundedEnd - silenceDuration);
                }
            }

            if (startValue is not null && boundedEnd > startValue.Value)
            {
                intervals.Add(new SilenceInterval
                {
                    StartSeconds = startValue.Value,
                    EndSeconds = boundedEnd
                });
            }

            openStart = null;
        }

        if (openStart is not null && sourceDurationSeconds > openStart.Value)
        {
            intervals.Add(new SilenceInterval
            {
                StartSeconds = openStart.Value,
                EndSeconds = sourceDurationSeconds
            });
        }

        return intervals;
    }

    private static bool TryParse(string value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    private static string Tail(string value, int lineCount)
    {
        var lines = value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        return string.Join(Environment.NewLine, lines.TakeLast(lineCount));
    }

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

    [GeneratedRegex(@"silence_start:\s*(-?\d+(?:\.\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex SilenceStartRegex();

    [GeneratedRegex(@"silence_end:\s*(-?\d+(?:\.\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex SilenceEndRegex();

    [GeneratedRegex(@"silence_duration:\s*(-?\d+(?:\.\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex SilenceDurationRegex();
}
