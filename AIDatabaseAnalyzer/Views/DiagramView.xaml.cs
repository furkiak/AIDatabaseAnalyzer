using AIDatabaseAnalyzer.ViewModels;

namespace AIDatabaseAnalyzer.Views;

public partial class DiagramView : ContentView
{
    public DiagramView()
    {
        InitializeComponent();
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
