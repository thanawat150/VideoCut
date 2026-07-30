namespace AutoCutStudio.Core.Services;

public static class VersionedPathService
{
    public static string GetNextAvailablePath(string directory, string fileNameWithoutExtension, string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileNameWithoutExtension);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);

        Directory.CreateDirectory(directory);

        var safeName = SanitizeFileName(fileNameWithoutExtension);
        var normalizedExtension = extension.StartsWith('.') ? extension : $".{extension}";
        var candidate = Path.Combine(directory, safeName + normalizedExtension);

        if (!File.Exists(candidate))
        {
            return candidate;
        }

        for (var version = 2; version < 100_000; version++)
        {
            candidate = Path.Combine(directory, $"{safeName}_v{version}{normalizedExtension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("Unable to allocate a versioned output file name.");
    }

    public static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "output" : sanitized;
    }
}
