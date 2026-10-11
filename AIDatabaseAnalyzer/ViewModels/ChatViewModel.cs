using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AIDatabaseAnalyzer.Services.Infrastructure;
using AIDatabaseAnalyzer.Services.Llm;
using AIDatabaseAnalyzer.Services.Reporting;

namespace AIDatabaseAnalyzer.ViewModels;

/// <summary>
/// "Ask AI": a conversation about the loaded schema. The schema sits in a stable system prompt,
/// so follow-up questions reuse the provider's prompt cache.
/// </summary>
public partial class ChatViewModel : ObservableObject
{
    /// <summary>Older turns are dropped from the request (not from the screen) to keep requests small.</summary>
    private const int MaxHistoryMessages = 16;

    private readonly WorkspaceSession _session;
    private readonly ISettingsService _settings;
    private readonly ILlmService _llm;
    private readonly IThemeService _theme;
    private readonly INoticeService _notices;
    private readonly IDialogService _dialogs;
    private readonly List<ChatEntry> _entries = new();
    private CancellationTokenSource? _cts;
    private object? _cloudConfirmedFor;
    private LlmUsage _usage;

    public ChatViewModel(WorkspaceSession session, ISettingsService settings, ILlmService llm, IThemeService theme, INoticeService notices, IDialogService dialogs)
    {
        _session = session;
        _settings = settings;
        _llm = llm;
        _theme = theme;
        _notices = notices;
        _dialogs = dialogs;

        foreach (string s in new[]
        {
            "Explain what this database is for and describe its core tables",
            "Write a query that lists the top 10 customers by total order value",
            "Which tables hold personal data, and how should they be protected?",
            "Which indexes would speed up the most common joins?",
            "How do I add a NOT NULL column to the largest table without downtime?",
            "Draw the main relationships as a text diagram"
        })
            Suggestions.Add(new FilterChip(s, s, chip => _ = SendAsync(chip.Key)));

        _session.SchemaChanged += Reset;
        _session.QuestionRequested += TakePendingQuestion;
        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(WorkspaceSession.CurrentSection) || _session.CurrentSection != AppSection.Chat) return;
            RefreshProvider();
            TakePendingQuestion();
            if (Html.Length == 0) Render();
        };
    }

    public ObservableCollection<FilterChip> Suggestions { get; } = new();

    [ObservableProperty] private string html = "";
    [ObservableProperty] private string input = "";
    [ObservableProperty] private string providerText = "";
    [ObservableProperty] private string usageText = "";

    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [ObservableProperty] private bool isBusy;

    [ObservableProperty] private bool isEmpty = true;

    public bool IsIdle => !IsBusy;

    private void TakePendingQuestion()
    {
        if (_session.TakeQuestion() is { } question) _ = SendAsync(question);
    }

    private void RefreshProvider()
    {
        var llm = _settings.GetLlmSettings();
        ProviderText = $"{llm.ProviderName} · {llm.ModelName}" + (llm.IsLocal ? " · local" : "");
    }

    private void Reset()
    {
        _cts?.Cancel();
        _entries.Clear();
        _usage = default;
        UsageText = "";
        IsEmpty = true;
        Html = "";
    }

    [RelayCommand]
    private Task Send() => SendAsync(Input);

    private async Task SendAsync(string? text)
    {
        string question = (text ?? "").Trim();
        if (question.Length == 0 || IsBusy || _session.Schema is not { } schema) return;

        await _settings.InitializeAsync();
        var llm = _settings.GetLlmSettings();
        RefreshProvider();

        if (llm.Info.NeedsApiKey && !llm.Info.ApiKeyOptional && string.IsNullOrWhiteSpace(llm.ApiKey))
        {
            _notices.Show($"Add your {llm.ProviderName} API key in Settings first.", NoticeKind.Error);
            _session.TryNavigate(AppSection.Settings);
            return;
        }

        if (!llm.IsLocal && !ReferenceEquals(_cloudConfirmedFor, schema))
        {
            if (!await _dialogs.ConfirmAsync("Send the schema to the cloud?",
                    $"Your questions and the schema of “{schema.DatabaseName}” (with masked sample rows, if extracted) will be sent to {llm.ProviderName}.",
                    "Send", "Cancel"))
                return;
            _cloudConfirmedFor = schema;
        }

        Input = "";
        IsEmpty = false;
        _entries.Add(new ChatEntry(LlmRole.User, question));
        IsBusy = true;
        Render(thinking: true, thinkingText: $"{llm.ProviderName} is thinking…");

        var cts = _cts = new CancellationTokenSource();
        try
        {
            string system = PromptBuilder.ChatSystemPrompt(schema, _session.SchemaJson, _session.Insights);
            var history = _entries
                .Where(e => !e.IsError)
                .Select(e => new LlmMessage(e.Role, e.Text))
                .TakeLast(MaxHistoryMessages)
                .SkipWhile(m => m.Role != LlmRole.User) // a conversation must start with the user
                .ToList();

            var result = await _llm.CompleteAsync(llm, new LlmRequest { System = system, Messages = history }, cts.Token);
            _usage += result.Usage;
            UsageText = _usage.TotalTokens > 0 ? $"This conversation: {_usage}" : "";
            _entries.Add(new ChatEntry(LlmRole.Assistant, result.Text, Meta: string.IsNullOrEmpty(result.Model) ? null : result.Model));
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            _entries.Add(new ChatEntry(LlmRole.Assistant, "Stopped.", IsError: true));
        }
        catch (Exception ex)
        {
            _entries.Add(new ChatEntry(LlmRole.Assistant, ex is LlmException ? ex.Message : ex.GetBaseException().Message, IsError: true));
        }
        finally
        {
            IsBusy = false;
            if (ReferenceEquals(_cts, cts)) _cts = null;
            Render();
        }
    }

    private void Render(bool thinking = false, string? thinkingText = null)
    {
        var schema = _session.Schema;
        Func<string, string?>? link = schema is null
            ? null
            : token => SchemaLinks.ResolveTable(token, schema) is { } table ? "aida://table/" + Uri.EscapeDataString(table) : null;
        Html = ChatRenderer.ToHtml(_entries, _theme.EffectiveTheme, link, thinking, thinkingText);
    }

    [RelayCommand]
    private void Stop() => _cts?.Cancel();

    [RelayCommand]
    private void Clear()
    {
        if (IsBusy) return;
        Reset();
        Render();
    }

    [RelayCommand]
    private async Task CopyConversationAsync()
    {
        if (_entries.Count == 0) return;
        string text = string.Join("\n\n", _entries.Select(e => (e.Role == LlmRole.User ? "**You:** " : "**AI:** ") + e.Text));
        await Clipboard.Default.SetTextAsync(text);
        _notices.Show("Conversation copied as Markdown.", NoticeKind.Success);
    }

    /// <summary>Handles "aida://" links of the chat page.</summary>
    public async Task HandleLinkAsync(string command, string argument)
    {
        switch (command)
        {
            case "table":
                _session.ShowInDiagram(argument);
                break;
            case "copy":
                await Clipboard.Default.SetTextAsync(argument);
                _notices.Show("Copied to the clipboard.", NoticeKind.Success);
                break;
        }
    }
}
