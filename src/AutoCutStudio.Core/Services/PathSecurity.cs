namespace AutoCutStudio.Core.Services;

public static class PathSecurity
{
    public static string ValidateMp4Source(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var fullPath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Source media does not exist.", fullPath);
        }

        if (!string.Equals(Path.GetExtension(fullPath), ".mp4", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("Phase 1 supports MP4 input only.");
        }

        return fullPath;
    }

    public static string EnsureUnderRoot(string candidatePath, string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        var fullCandidate = Path.GetFullPath(candidatePath);
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var rootWithSeparator = fullRoot + Path.DirectorySeparatorChar;
        if (!fullCandidate.StartsWith(rootWithSeparator, comparison) &&
            !string.Equals(fullCandidate, fullRoot, comparison))
        {
            throw new UnauthorizedAccessException("The path is outside the allowed project root.");
        }

        return fullCandidate;
    }

    public static void ValidateTimelineSegments(IEnumerable<Models.TimelineSegment> segments, double sourceDurationSeconds)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var materialized = segments.ToList();
        if (materialized.Count == 0)
        {
            throw new InvalidOperationException("The timeline has no segments to export.");
        }

        foreach (var segment in materialized)
        {
            if (segment.StartSeconds < 0 ||
                segment.EndSeconds <= segment.StartSeconds ||
                segment.EndSeconds > sourceDurationSeconds + 0.05)
            {
                throw new InvalidOperationException(
                    $"Invalid timeline segment {segment.StartSeconds:0.###}-{segment.EndSeconds:0.###}.");
            }
        }
    }
}
