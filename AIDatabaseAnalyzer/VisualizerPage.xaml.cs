using AIDatabaseAnalyzer.ViewModels;
using System.Text;
using AIDatabaseAnalyzer.Services;
using System.Text.Json;

namespace AIDatabaseAnalyzer;

public partial class VisualizerPage : ContentPage
{
    private VisualizerViewModel _viewModel;

    public VisualizerPage(string fullJsonSchema)
    {
        InitializeComponent();
        _viewModel = new VisualizerViewModel(fullJsonSchema);
        BindingContext = _viewModel;
    }

    private async void OnWebViewNavigating(object sender, WebNavigatingEventArgs e)
    {
        if (e.Url.StartsWith("app://tableclick", StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
            var uri = new Uri(e.Url);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            string tableName = query.Get("name") ?? "";

            await ShowTableDetails(tableName);
        }
    }

    private async Task ShowTableDetails(string tableName)
    {
        try
        {
            var doc = JsonDocument.Parse(_viewModel.FullJsonSchema);
            if (doc.RootElement.TryGetProperty("SampleData", out var sampleData) &&
                sampleData.TryGetProperty(tableName, out var dataRows))
            {
                var sb = new StringBuilder();
                sb.AppendLine($"{tableName} - Sample Records:\n");

                foreach (var row in dataRows.EnumerateArray().Take(3))
                {
                    sb.AppendLine("--------------------------");
                    foreach (var prop in row.EnumerateObject())
                    {
                        sb.AppendLine($"{prop.Name}: {prop.Value}");
                    }
                }
                await this.DisplayAppAlertAsync("Table Details", sb.ToString(), "Close");
            }
            else
            {
                await this.DisplayAppAlertAsync("Info", $"No sample data found for {tableName}.", "OK");
            }
        }
        catch (Exception)
        {
            await this.DisplayAppAlertAsync("Error", "Could not load local table details.", "OK");
        }
    }
}
