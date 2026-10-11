using AIDatabaseAnalyzer.Services.Infrastructure;
using AIDatabaseAnalyzer.ViewModels;

namespace AIDatabaseAnalyzer.Views;

public partial class ReportsView : ContentView
{
    public ReportsView()
    {
        InitializeComponent();
        Layouts.ConstrainWidth(this, Column, 1000);

        // Table names open the diagram, code blocks copy through the app, web links open in the browser.
        HostLinks.Attach(Viewer, (command, argument) =>
            BindingContext is ReportsViewModel viewModel ? viewModel.HandleLinkAsync(command, argument) : Task.CompletedTask);
    }
}
