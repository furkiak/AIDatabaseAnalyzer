using AIDatabaseAnalyzer.Services.Infrastructure;

namespace AIDatabaseAnalyzer.Views;

public partial class SettingsView : ContentView
{
    public SettingsView()
    {
        InitializeComponent();
        Layouts.ConstrainWidth(this, Column, 860);
    }
}
