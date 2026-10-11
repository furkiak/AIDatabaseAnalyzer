using AIDatabaseAnalyzer.Services.Infrastructure;

namespace AIDatabaseAnalyzer.Views;

public partial class AnalysisView : ContentView
{
    public AnalysisView()
    {
        InitializeComponent();
        Layouts.ConstrainWidth(this, Column, 1000);
    }
}
