using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using AutoCutStudio.Core.Interfaces;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;

namespace AutoCutStudio.Infrastructure;

public sealed class FfprobeMediaProbe : IMediaProbe
{
    private readonly IToolLocator _toolLocator;

    public FfprobeMediaProbe(IToolLocator toolLocator)
    {
        _toolLocator = toolLocator;
    }

    public async Task<MediaMetadata> ProbeAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Media file was not found.", fullPath);
        }

        var tools = _toolLocator.Locate();
        if (!tools.IsReady || string.IsNullOrWhiteSpace(tools.FfprobePath))
        {
            throw new InvalidOperationException(tools.Message);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = tools.FfprobePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in new[]
                 {
                     "-v", "error",
                     "-print_format", "json",
                     "-show_format",
                     "-show_streams",
                     fullPath
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("FFprobe could not be started.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidDataException($"FFprobe failed: {error.Trim()}");
        }

        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        var streams = root.TryGetProperty("streams", out var streamArray)
            ? streamArray.EnumerateArray().ToArray()
            : [];

        var video = streams.FirstOrDefault(stream =>
            string.Equals(GetString(stream, "codec_type"), "video", StringComparison.OrdinalIgnoreCase));
        var audio = streams.FirstOrDefault(stream =>
            string.Equals(GetString(stream, "codec_type"), "audio", StringComparison.OrdinalIgnoreCase));

        var hasVideo = video.ValueKind != JsonValueKind.Undefined;
        var hasAudio = audio.ValueKind != JsonValueKind.Undefined;

        var format = root.TryGetProperty("format", out var formatElement)
            ? formatElement
            : default;

        var avgFrameRate = hasVideo ? ParseFraction(GetString(video, "avg_frame_rate")) : 0;
        var nominalFrameRate = hasVideo ? ParseFraction(GetString(video, "r_frame_rate")) : 0;

        return new MediaMetadata
        {
            SourcePath = fullPath,
            FileSizeBytes = new FileInfo(fullPath).Length,
            Container = GetString(format, "format_name") ?? string.Empty,
            VideoCodec = hasVideo ? GetString(video, "codec_name") : null,
            AudioCodec = hasAudio ? GetString(audio, "codec_name") : null,
            HasVideo = hasVideo,
            HasAudio = hasAudio,
            Width = hasVideo ? GetInt(video, "width") : 0,
            Height = hasVideo ? GetInt(video, "height") : 0,
            FrameRate = avgFrameRate > 0 ? avgFrameRate : nominalFrameRate,
            DurationSeconds = ParseDouble(GetString(format, "duration")),
            BitRate = ParseLong(GetString(format, "bit_rate")),
            AudioChannels = hasAudio ? GetNullableInt(audio, "channels") : null,
            AudioSampleRate = hasAudio ? ParseNullableInt(GetString(audio, "sample_rate")) : null,
            Rotation = hasVideo ? GetRotation(video) : 0,
            PixelFormat = hasVideo ? GetString(video, "pix_fmt") : null,
            ColorSpace = hasVideo ? GetString(video, "color_space") : null,
            IsVariableFrameRate = avgFrameRate > 0 &&
                                  nominalFrameRate > 0 &&
                                  Math.Abs(avgFrameRate - nominalFrameRate) > 0.01,
            ProbeJson = output,
            ProbedAt = DateTimeOffset.UtcNow
        };
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null
        };
    }

    private static int GetInt(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
            ? value
            : 0;

    private static int? GetNullableInt(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
            ? value
            : null;

    private static int? ParseNullableInt(string? value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static long? ParseLong(string? value)
        => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static double ParseDouble(string? value)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;

    private static double ParseFraction(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var parts = value.Split('/');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) &&
            Math.Abs(denominator) > double.Epsilon)
        {
            return numerator / denominator;
        }

        return ParseDouble(value);
    }

    private static int GetRotation(JsonElement video)
    {
        if (video.TryGetProperty("tags", out var tags) &&
            tags.ValueKind == JsonValueKind.Object &&
            tags.TryGetProperty("rotate", out var rotateTag) &&
            int.TryParse(rotateTag.GetString(), out var tagRotation))
        {
            return tagRotation;
        }

        if (video.TryGetProperty("side_data_list", out var sideData) &&
            sideData.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in sideData.EnumerateArray())
            {
                if (item.TryGetProperty("rotation", out var rotation) &&
                    rotation.TryGetInt32(out var value))
                {
                    return value;
                }
            }
        }

        return 0;
    }
}
