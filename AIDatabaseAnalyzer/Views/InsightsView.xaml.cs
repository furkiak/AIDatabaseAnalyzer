using AIDatabaseAnalyzer.Services.Infrastructure;

namespace AIDatabaseAnalyzer.Views;

public partial class InsightsView : ContentView
{
    public InsightsView()
    {
        InitializeComponent();
        Layouts.ConstrainWidth(this, Column, 1000);
    }
}
