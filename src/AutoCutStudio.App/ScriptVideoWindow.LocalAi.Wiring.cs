using System.Windows;

namespace AutoCutStudio.App;

public partial class ScriptVideoWindow
{
    private bool _localAiHandlersWired;

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        Loaded += WireLocalAiHandlersOnLoaded;
    }

    private void WireLocalAiHandlersOnLoaded(object sender, RoutedEventArgs e)
    {
        if (_localAiHandlersWired)
            return;
        _localAiHandlersWired = true;

        GenerateVisualsButton.Click -= GenerateVisuals_Click;
        GenerateVisualsButton.Click += GenerateVisualsLocalAware_Click;
        CreateButton.Click -= Create_Click;
        CreateButton.Click += CreateLocalAware_Click;
        VisualProviderComboBox.SelectionChanged += async (_, _) => await RefreshLocalProviderStateAsync();
        VoiceProviderComboBox.SelectionChanged += async (_, _) => await RefreshLocalProviderStateAsync();
    }
}
