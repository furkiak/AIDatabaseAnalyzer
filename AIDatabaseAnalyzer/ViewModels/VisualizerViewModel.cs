using AIDatabaseAnalyzer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIDatabaseAnalyzer.ViewModels;

public partial class VisualizerViewModel : ObservableObject
{
    [ObservableProperty] public partial string HtmlSource { get; set; } = string.Empty;
    [ObservableProperty] public partial string FullJsonSchema { get; set; } = string.Empty;

    public VisualizerViewModel(string fullJsonSchema)
    {
        FullJsonSchema = fullJsonSchema;
        HtmlSource = SchemaDiagramHtml.Create(fullJsonSchema);
    }

    [RelayCommand]
    private async Task CloseAsync() => await Shell.Current.Navigation.PopModalAsync();
}
