using AIDatabaseAnalyzer.Services.Infrastructure;

namespace AIDatabaseAnalyzer;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services, ISettingsService settings, IThemeService theme)
    {
        InitializeComponent();
        _services = services;
        theme.Apply(settings.Theme);
    }

    // The page is resolved here (not in the constructor) because it needs the application resources to be loaded first.
    protected override Window CreateWindow(IActivationState? activationState) =>
        new(_services.GetRequiredService<MainPage>())
        {
            Title = "AI Database Analyzer",
            Width = 1240,
            Height = 840,
            MinimumWidth = 1020,
            MinimumHeight = 700
        };
}
