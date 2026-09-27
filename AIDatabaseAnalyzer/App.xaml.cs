namespace AIDatabaseAnalyzer;

public partial class App : Application
{
    public Services.AppearanceController Appearance { get; }
    private readonly Services.AppProfile _profile;

    public App(Services.AppProfile profile, Services.AppearanceController appearance)
    {
        InitializeComponent();
        _profile = profile;
        Appearance = appearance;
        Appearance.ApplySaved();
        MainPage = new AppShell();
    }

    protected override Window CreateWindow(IActivationState activationState)
    {
        var window = base.CreateWindow(activationState);
        window.Title = _profile.IsIsolated ? "AI Database Analyzer — ISOLATED PROFILE" : "AI Database Analyzer";
         
        const int newWidth = 1024;
        const int newHeight = 850;

        window.Width = newWidth;
        window.Height = newHeight;
        window.MinimumWidth = 400;
        window.MinimumHeight = 400;

#if WINDOWS
        window.Created += (_, _) => Platforms.Windows.NativeAppearance.ApplyWindow(window);
#endif

        return window;
    }
}
