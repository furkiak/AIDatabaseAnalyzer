using AIDatabaseAnalyzer.Services.Infrastructure;
using AIDatabaseAnalyzer.ViewModels;

namespace AIDatabaseAnalyzer.Views;

public partial class DiagramView : ContentView
{
    public DiagramView()
    {
        InitializeComponent();

        // The page talks to the app through "aida://command/argument" navigations.
        HostLinks.Attach(Browser, (command, argument) =>
            BindingContext is DiagramViewModel viewModel ? viewModel.HandleLinkAsync(command, argument) : Task.CompletedTask);
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        if (BindingContext is DiagramViewModel viewModel)
            viewModel.SvgProvider = ReadSvgAsync;
    }

    /// <summary>Asks the diagram page for its SVG (Base64, so quoting differences between platforms do not matter).</summary>
    private async Task<string?> ReadSvgAsync()
    {
        string? result = await MainThread.InvokeOnMainThreadAsync(() => Browser.EvaluateJavaScriptAsync("getSvgBase64()"));
        return result?.Trim().Trim('"');
    }
}
