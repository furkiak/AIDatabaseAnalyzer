using AIDatabaseAnalyzer.Services.Infrastructure;

namespace AIDatabaseAnalyzer.Views;

public partial class ReportsView : ContentView
{
    public ReportsView()
    {
        InitializeComponent();
        Layouts.ConstrainWidth(this, Column, 1000);

        // Links inside a report open in the user's browser instead of replacing the report.
        Viewer.Navigating += (_, e) =>
        {
            if (e.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || e.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || e.Url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                _ = Launcher.Default.TryOpenAsync(new Uri(e.Url));
            }
        };
    }
}
