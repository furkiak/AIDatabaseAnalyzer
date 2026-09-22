using AIDatabaseAnalyzer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIDatabaseAnalyzer.ViewModels;

public partial class VisualizerViewModel : ObservableObject
{
    [ObservableProperty] private string htmlSource;
    [ObservableProperty] private string fullJsonSchema;

    public VisualizerViewModel(string fullJsonSchema)
    {
        FullJsonSchema = fullJsonSchema;
        HtmlSource = SchemaDiagramHtml.Create(fullJsonSchema);
    }

    [RelayCommand]
    private async Task CloseAsync() => await Shell.Current.Navigation.PopModalAsync();
}
