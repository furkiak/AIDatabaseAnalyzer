using AIDatabaseAnalyzer.Services.Infrastructure;
using AIDatabaseAnalyzer.ViewModels;

namespace AIDatabaseAnalyzer.Views;

public partial class SnapshotsView : ContentView
{
    public SnapshotsView()
    {
        InitializeComponent();
        Layouts.ConstrainWidth(this, Column, 900);

        // Table names in the list of changes open the ER diagram.
        HostLinks.Attach(Changes, (command, argument) =>
            BindingContext is SnapshotsViewModel viewModel ? viewModel.HandleLinkAsync(command, argument) : Task.CompletedTask);
    }
}
