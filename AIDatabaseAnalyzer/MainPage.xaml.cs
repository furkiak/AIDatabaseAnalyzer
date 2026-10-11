using System.ComponentModel;
using AIDatabaseAnalyzer.ViewModels;

namespace AIDatabaseAnalyzer;

public partial class MainPage : ContentPage
{
    private readonly ShellViewModel _shell;
    private AppSection _shown = AppSection.Connect;

    public MainPage(
        ShellViewModel shell,
        ConnectViewModel connect,
        ScopeViewModel scope,
        InsightsViewModel insights,
        AnalysisViewModel analysis,
        ReportsViewModel reports,
        SettingsViewModel settings,
        DiagramViewModel diagram,
        ChatViewModel chat,
        SnapshotsViewModel snapshots)
    {
        InitializeComponent();

        _shell = shell;
        BindingContext = shell;
        ConnectPanel.BindingContext = connect;
        ScopePanel.BindingContext = scope;
        InsightsPanel.BindingContext = insights;
        AnalysisPanel.BindingContext = analysis;
        ReportsPanel.BindingContext = reports;
        SettingsPanel.BindingContext = settings;
        DiagramPanel.BindingContext = diagram;
        ChatPanel.BindingContext = chat;
        SnapshotsPanel.BindingContext = snapshots;

        shell.PropertyChanged += OnShellPropertyChanged;
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ShellViewModel.Current) || _shell.Current == _shown) return;
        _shown = _shell.Current;

        View panel = _shown switch
        {
            AppSection.Connect => ConnectPanel,
            AppSection.Scope => ScopePanel,
            AppSection.Insights => InsightsPanel,
            AppSection.Analysis => AnalysisPanel,
            AppSection.Reports => ReportsPanel,
            AppSection.Settings => SettingsPanel,
            AppSection.Chat => ChatPanel,
            AppSection.Snapshots => SnapshotsPanel,
            _ => DiagramPanel
        };

        _ = FadeInAsync(panel);
    }

    /// <summary>A short fade and lift when a screen appears — enough to feel fluid without getting in the way.</summary>
    private static async Task FadeInAsync(View panel)
    {
        panel.Opacity = 0;
        panel.TranslationY = 10;
        await Task.WhenAll(panel.FadeTo(1, 220, Easing.CubicOut), panel.TranslateTo(0, 0, 260, Easing.CubicOut));
    }
}
