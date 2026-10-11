using AIDatabaseAnalyzer.Services.Infrastructure;

namespace AIDatabaseAnalyzer.Views;

public partial class ScopeView : ContentView
{
    public ScopeView()
    {
        InitializeComponent();
        Layouts.ConstrainWidth(this, Column, 1000);
    }
}
