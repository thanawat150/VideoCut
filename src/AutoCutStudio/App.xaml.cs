using System.Windows;

namespace AutoCutStudio;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--worker", StringComparer.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunWorkerAndExitAsync(e.Args.Contains("--once", StringComparer.OrdinalIgnoreCase));
            return;
        }

        base.OnStartup(e);
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    private async Task RunWorkerAndExitAsync(bool runOnce)
    {
        try
        {
            await JobWorker.RunAsync(runOnce);
            Shutdown(0);
        }
        catch (Exception ex)
        {
            try
            {
                var jobsRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AutoCutStudio",
                    "Jobs");
                Directory.CreateDirectory(jobsRoot);
                await File.AppendAllTextAsync(
                    Path.Combine(jobsRoot, "worker-crash.log"),
                    $"{DateTimeOffset.Now:O}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            }
            catch
            {
                // The original worker error remains the primary failure.
            }

            Shutdown(1);
        }
    }
}
