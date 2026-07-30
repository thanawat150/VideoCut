using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace AutoCutStudio.App;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                if (Current.MainWindow is AutoCutStudio.App.MainWindow window)
                {
                    window.EnableAutomaticEditingUi();
                    window.EnableSpeechEditingUi();
                }
            }));
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs eventArgs)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogDirectory);
            File.AppendAllText(
                Path.Combine(AppPaths.LogDirectory, "app-crash.log"),
                $"[{DateTimeOffset.UtcNow:O}]{Environment.NewLine}{eventArgs.Exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Do not hide the original exception if crash logging fails.
        }

        MessageBox.Show(
            $"เกิดข้อผิดพลาดที่ไม่ได้คาดไว้\n\n{eventArgs.Exception.Message}\n\nSource Media ไม่ถูกแก้ไข",
            "AutoCut Studio",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        eventArgs.Handled = true;
    }
}
