namespace AutoCutStudio.App;

internal static class AppPaths
{
    public static string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoCutStudio");

    public static string SettingsDirectory { get; } = Path.Combine(RootDirectory, "Settings");
    public static string CacheDirectory { get; } = Path.Combine(RootDirectory, "Cache");
    public static string LogDirectory { get; } = Path.Combine(RootDirectory, "Logs");
    public static string ModelDirectory { get; } = Path.Combine(RootDirectory, "Models");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(SettingsDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(ModelDirectory);
    }
}
