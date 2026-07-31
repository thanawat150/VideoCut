using System.Windows;

namespace AutoCutStudio.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppPaths.EnsureCreated();
        base.OnStartup(e);
        Dispatcher.BeginInvoke(() =>
        {
            if (MainWindow is MainWindow window)
            {
                window.EnableVisualAutomationUi();
                window.EnableAutomaticEditingUi();
                window.EnableSpeechEditingUi();
                window.EnableSocialAutomationUi();
                window.EnableEnhancementUi();
                window.EnableAdvancedAiUi();
                window.EnableProfessionalToolsUi();
            }
        });
    }
}
