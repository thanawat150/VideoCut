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
        catch
        {
            Shutdown(1);
        }
    }
}
