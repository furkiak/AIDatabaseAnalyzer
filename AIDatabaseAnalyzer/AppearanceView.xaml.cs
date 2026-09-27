namespace AIDatabaseAnalyzer;

public partial class AppearanceView : ContentView
{
    public AppearanceView()
    {
        InitializeComponent();
        BindingContext = ((App)Application.Current!).Appearance;
    }
}
