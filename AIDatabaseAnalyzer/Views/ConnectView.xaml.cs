using AIDatabaseAnalyzer.Services.Infrastructure;

namespace AIDatabaseAnalyzer.Views;

public partial class ConnectView : ContentView
{
    public ConnectView()
    {
        InitializeComponent();
        Layouts.ConstrainWidth(this, Column, 1000);
    }
}
