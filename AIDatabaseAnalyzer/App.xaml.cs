namespace AIDatabaseAnalyzer;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        MainPage = new AppShell();
    }

    protected override Window CreateWindow(IActivationState activationState)
    {
        var window = base.CreateWindow(activationState);
         
        const int newWidth = 1024;
        const int newHeight = 850;

        window.Width = newWidth;
        window.Height = newHeight;
        window.MinimumWidth = 1024;
        window.MinimumHeight = 768;

        return window;
    }
}