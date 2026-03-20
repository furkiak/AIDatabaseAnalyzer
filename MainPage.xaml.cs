using AIDatabaseAnalyzer.ViewModels;

namespace AIDatabaseAnalyzer;

public partial class MainPage : ContentPage
{
    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}