using AutoCutStudio.Core.Models;

namespace AutoCutStudio.Core.Services;

public sealed class AutomaticEditPlanner
{
    public AutomaticEditPlan BuildRemoveSilencePlan(
        string command,
        string inputPath,
        double sourceDurationSeconds,
        SilenceDetectionOptions options,
        IReadOnlyList<SilenceInterval> detectedSilence,
        string detectionCommand)
    {
        if (sourceDurationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceDurationSeconds),
                "Source duration must be greater than zero.");
        }

        var normalizedSilence = NormalizeSilence(detectedSilence, sourceDurationSeconds);
        var removable = normalizedSilence
            .Select(interval => new SilenceInterval
            {
                StartSeconds = Math.Clamp(
                    interval.StartSeconds + options.EdgePaddingSeconds,
                    0,
                    sourceDurationSeconds),
                EndSeconds = Math.Clamp(
                    interval.EndSeconds - options.EdgePaddingSeconds,
                    0,
                    sourceDurationSeconds)
            })
            .Where(interval => interval.DurationSeconds >= 0.02)
            .ToList();

        var proposed = BuildComplement(removable, sourceDurationSeconds)
            .Where(segment => segment.DurationSeconds >= options.MinimumOutputSegmentSeconds)
            .ToList();

        var outputDuration = proposed.Sum(segment => segment.DurationSeconds);
        var removedDuration = Math.Max(0, sourceDurationSeconds - outputDuration);
        var warnings = new List<string>();

        if (normalizedSilence.Count == 0)
        {
            warnings.Add("ไม่พบช่วงเงียบที่ยาวถึงเกณฑ์ จึงยังไม่เปลี่ยน Timeline");
        }
        else if (removable.Count == 0)
        {
            warnings.Add("พบช่วงเงียบ แต่สั้นเกินกว่าจะตัดหลังเผื่อขอบเสียงพูด");
        }

        if (proposed.Count == 0)
        {
            warnings.Add("แผนนี้จะไม่เหลือวิดีโอ จึงไม่อนุญาตให้นำไปใช้");
        }

        return new AutomaticEditPlan
        {
            Command = command,
            InputPath = inputPath,
            Options = options,
            DetectedSilence = normalizedSilence,
            ProposedSegments = proposed,
            SourceDurationSeconds = sourceDurationSeconds,
            OutputDurationSeconds = outputDuration,
            RemovedDurationSeconds = removedDuration,
            IsActionable = proposed.Count > 0 && removedDuration >= 0.02,
            Warnings = warnings,
            DetectionCommand = detectionCommand
        };
    }

    private static List<SilenceInterval> NormalizeSilence(
        IReadOnlyList<SilenceInterval> intervals,
        double duration)
    {
        var ordered = intervals
            .Select(interval => new SilenceInterval
            {
                StartSeconds = Math.Clamp(interval.StartSeconds, 0, duration),
                EndSeconds = Math.Clamp(interval.EndSeconds, 0, duration)
            })
            .Where(interval => interval.EndSeconds > interval.StartSeconds)
            .OrderBy(interval => interval.StartSeconds)
            .ThenBy(interval => interval.EndSeconds)
            .ToList();

        var merged = new List<SilenceInterval>();
        foreach (var interval in ordered)
        {
            if (merged.Count == 0 || interval.StartSeconds > merged[^1].EndSeconds + 0.001)
            {
                merged.Add(interval);
                continue;
            }

            var previous = merged[^1];
            merged[^1] = previous with
            {
                EndSeconds = Math.Max(previous.EndSeconds, interval.EndSeconds)
            };
        }

        return merged;
    }

    private static List<TimelineSegment> BuildComplement(
        IReadOnlyList<SilenceInterval> removable,
        double duration)
    {
        var segments = new List<TimelineSegment>();
        var cursor = 0d;

        foreach (var interval in removable)
        {
            if (interval.StartSeconds > cursor + 0.001)
            {
                segments.Add(new TimelineSegment
                {
                    StartSeconds = cursor,
                    EndSeconds = interval.StartSeconds
                });
            }

            cursor = Math.Max(cursor, interval.EndSeconds);
        }

        if (cursor < duration - 0.001)
        {
            segments.Add(new TimelineSegment
            {
                StartSeconds = cursor,
                EndSeconds = duration
            });
        }

        return segments;
    }
}
