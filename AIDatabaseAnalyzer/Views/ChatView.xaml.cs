using AIDatabaseAnalyzer.Services.Infrastructure;
using AIDatabaseAnalyzer.ViewModels;

namespace AIDatabaseAnalyzer.Views;

public partial class ChatView : ContentView
{
    public ChatView()
    {
        InitializeComponent();

        // Table names open the diagram, code blocks are copied through the app.
        HostLinks.Attach(Conversation, (command, argument) =>
            BindingContext is ChatViewModel viewModel ? viewModel.HandleLinkAsync(command, argument) : Task.CompletedTask);
    }
}
