using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AIDatabaseAnalyzer.Models;
using AIDatabaseAnalyzer.Services.Database;
using AIDatabaseAnalyzer.Services.Infrastructure;
using AIDatabaseAnalyzer.Services.Llm;
using AIDatabaseAnalyzer.Services.Reporting;

namespace AIDatabaseAnalyzer.ViewModels;

public sealed class CustomTopicDto
{
    public string Title { get; set; } = "";
    public string Prompt { get; set; } = "";
}

[JsonSerializable(typeof(List<CustomTopicDto>))]
internal sealed partial class CustomTopicJsonContext : JsonSerializerContext
{
}

/// <summary>Step 4: choose the questions, run the AI analysis with live progress, cancel or retry.</summary>
public partial class AnalysisViewModel : ObservableObject
{
    private const string CustomCategory = "Custom";
    private const string AllCategory = "All";
    private const string CustomTopicsKey = "CustomTopics";

    private readonly WorkspaceSession _session;
    private readonly ISettingsService _settings;
    private readonly IAnalysisRunner _runner;
    private readonly IReportStore _reports;
    private readonly IPdfReportService _pdf;
    private readonly INoticeService _notices;
    private readonly IDialogService _dialogs;

    private CancellationTokenSource? _runCts;
    private AnalysisReport? _lastReport;
    private string _categoryFilter = AllCategory;
    private object? _cloudConfirmedFor;

    public AnalysisViewModel(
        WorkspaceSession session,
        ISettingsService settings,
        IAnalysisRunner runner,
        IReportStore reports,
        IPdfReportService pdf,
        INoticeService notices,
        IDialogService dialogs)
    {
        _session = session;
        _settings = settings;
        _runner = runner;
        _reports = reports;
        _pdf = pdf;
        _notices = notices;
        _dialogs = dialogs;

        foreach (var topic in TopicCatalog.CreateDefaults()) AddTopic(topic);
        LoadCustomTopics();

        CategoryChips.Add(new FilterChip(AllCategory, AllCategory, Select) { IsSelected = true });
        foreach (string category in TopicCatalog.Categories)
            CategoryChips.Add(new FilterChip(category, category, Select));
        CategoryChips.Add(new FilterChip(CustomCategory, CustomCategory, Select));

        languageIndex = Math.Max(0, Array.IndexOf(PromptBuilder.Languages, settings.ReportLanguage));

        _session.SchemaChanged += OnSchemaChanged;
        _session.PropertyChanged += OnSessionPropertyChanged;
        RefreshCategoryCounts();
        ApplyFilter();
        UpdateSelectionInfo();
        RefreshProviderInfo();
    }

    public IReadOnlyList<string> Languages => PromptBuilder.Languages;

    public ObservableCollection<AnalysisTopic> Topics { get; } = new();
    public ObservableCollection<FilterChip> CategoryChips { get; } = new();
    public ObservableCollection<AnalysisTopic> RunItems { get; } = new();

    [ObservableProperty] private IReadOnlyList<AnalysisTopic> visibleTopics = Array.Empty<AnalysisTopic>();
    [ObservableProperty] private string searchQuery = "";
    [ObservableProperty] private int languageIndex;
    [ObservableProperty] private string customPrompt = "";

    [ObservableProperty] private string selectionText = "";
    [ObservableProperty] private string estimateText = "";
    [ObservableProperty] private string providerText = "";
    [ObservableProperty] private bool providerWarningVisible;
    [ObservableProperty] private string providerWarning = "";

    [NotifyPropertyChangedFor(nameof(IsConfiguring))]
    [ObservableProperty] private bool isRunning;

    [NotifyPropertyChangedFor(nameof(IsConfiguring))]
    [ObservableProperty] private bool hasRun;

    [ObservableProperty] private double progress;
    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private string resultText = "";
    [ObservableProperty] private bool canRetry;
    [ObservableProperty] private bool hasResult;

    public bool IsConfiguring => !IsRunning && !HasRun;

    // ------------------------------------------------------------------ topics

    private void AddTopic(AnalysisTopic topic)
    {
        topic.PropertyChanged += OnTopicChanged;
        topic.RemoveRequested += RemoveCustomTopic;
        Topics.Add(topic);
    }

    private void OnTopicChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AnalysisTopic.IsSelected)) UpdateSelectionInfo();
    }

    private void Select(FilterChip chip)
    {
        _categoryFilter = chip.Key;
        foreach (var c in CategoryChips) c.IsSelected = c == chip;
        ApplyFilter();
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    partial void OnLanguageIndexChanged(int value)
    {
        if (value >= 0 && value < PromptBuilder.Languages.Length) _settings.ReportLanguage = PromptBuilder.Languages[value];
    }

    private void ApplyFilter()
    {
        string query = SearchQuery.Trim();
        VisibleTopics = Topics
            .Where(t => _categoryFilter == AllCategory
                        || (_categoryFilter == CustomCategory ? t.IsCustom : t.Category == _categoryFilter))
            .Where(t => query.Length == 0
                        || t.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || t.Prompt.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void RefreshCategoryCounts()
    {
        foreach (var chip in CategoryChips)
        {
            chip.Count = chip.Key == AllCategory ? Topics.Count
                : chip.Key == CustomCategory ? Topics.Count(t => t.IsCustom)
                : Topics.Count(t => t.Category == chip.Key);
        }
    }

    private void UpdateSelectionInfo()
    {
        int selected = Topics.Count(t => t.IsSelected);
        SelectionText = $"{selected} of {Topics.Count} topics selected";

        int schemaTokens = SchemaSerializer.EstimateTokens(_session.SchemaJson);
        long total = (long)selected * (schemaTokens + 500) + 2L * 6000;
        EstimateText = selected == 0
            ? "Select at least one topic."
            : $"≈ {FormatTokens(total)} input tokens in total ({FormatTokens(schemaTokens)} schema context per topic — identical prefixes are cached by Gemini and Ollama).";
    }

    private static string FormatTokens(long tokens) =>
        tokens >= 1_000_000 ? $"{tokens / 1_000_000.0:0.0}M" : tokens >= 1000 ? $"{tokens / 1000.0:0.#}k" : tokens.ToString();

    [RelayCommand]
    private void SelectVisible()
    {
        foreach (var t in VisibleTopics) t.IsSelected = true;
    }

    [RelayCommand]
    private void ClearVisible()
    {
        foreach (var t in VisibleTopics) t.IsSelected = false;
    }

    [RelayCommand]
    private void AddCustom()
    {
        string text = CustomPrompt.Trim();
        if (text.Length == 0) return;

        string title = text.Length <= 60 ? text : text[..57] + "…";
        AddTopic(new AnalysisTopic { Title = "Custom: " + title, Prompt = text, Category = CustomCategory, IsCustom = true, IsSelected = true });
        CustomPrompt = "";
        SaveCustomTopics();
        RefreshCategoryCounts();
        ApplyFilter();
        UpdateSelectionInfo();
        _notices.Show("Custom prompt added.", NoticeKind.Success);
    }

    private void RemoveCustomTopic(AnalysisTopic topic)
    {
        topic.PropertyChanged -= OnTopicChanged;
        topic.RemoveRequested -= RemoveCustomTopic;
        Topics.Remove(topic);
        SaveCustomTopics();
        RefreshCategoryCounts();
        ApplyFilter();
        UpdateSelectionInfo();
    }

    private void SaveCustomTopics()
    {
        var dto = Topics.Where(t => t.IsCustom).Select(t => new CustomTopicDto { Title = t.Title, Prompt = t.Prompt }).ToList();
        Preferences.Default.Set(CustomTopicsKey, JsonSerializer.Serialize(dto, CustomTopicJsonContext.Default.ListCustomTopicDto));
    }

    private void LoadCustomTopics()
    {
        try
        {
            string json = Preferences.Default.Get(CustomTopicsKey, "");
            if (string.IsNullOrWhiteSpace(json)) return;
            var list = JsonSerializer.Deserialize(json, CustomTopicJsonContext.Default.ListCustomTopicDto);
            if (list is null) return;
            foreach (var item in list)
                AddTopic(new AnalysisTopic { Title = item.Title, Prompt = item.Prompt, Category = CustomCategory, IsCustom = true, IsSelected = false });
        }
        catch (JsonException)
        {
            // ignore a damaged value; the user simply starts without custom prompts
        }
    }

    // ------------------------------------------------------------------ session / provider

    private void OnSchemaChanged()
    {
        _lastReport = null;
        HasRun = false;
        HasResult = false;
        RunItems.Clear();
        foreach (var t in Topics) t.Status = AnalysisStatus.Pending;
        UpdateSelectionInfo();
        RefreshProviderInfo();
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceSession.CurrentSection) && _session.CurrentSection == AppSection.Analysis)
        {
            RefreshProviderInfo();
            UpdateSelectionInfo();
        }
    }

    /// <summary>Re-reads the provider choice (it may have changed in Settings).</summary>
    public void RefreshProviderInfo()
    {
        var llm = _settings.GetLlmSettings();
        ProviderText = $"{llm.ProviderName} · {llm.ModelName}";

        if (llm.Provider == LlmProvider.Gemini && string.IsNullOrWhiteSpace(llm.GeminiApiKey))
        {
            ProviderWarning = "Gemini needs an API key. Open Settings to add it, or switch to a local Ollama model.";
            ProviderWarningVisible = true;
        }
        else if (!llm.IsLocal)
        {
            ProviderWarning = "Cloud model: schema metadata (and sample rows, if extracted) are sent to Google. Use Ollama to keep everything on this machine.";
            ProviderWarningVisible = true;
        }
        else
        {
            ProviderWarningVisible = false;
        }
    }

    [RelayCommand]
    private void OpenSettings() => _session.TryNavigate(AppSection.Settings);

    // ------------------------------------------------------------------ run

    [RelayCommand]
    private Task RunAsync() => RunCoreAsync(retry: false);

    [RelayCommand]
    private Task RetryFailedAsync() => RunCoreAsync(retry: true);

    private async Task RunCoreAsync(bool retry)
    {
        if (IsRunning || _session.Schema is not { } schema) return;

        var topics = retry && RunItems.Count > 0 ? RunItems.ToList() : Topics.Where(t => t.IsSelected).ToList();
        if (topics.Count == 0)
        {
            _notices.Show("Select at least one topic first.", NoticeKind.Warning);
            return;
        }

        await _settings.InitializeAsync();
        var llm = _settings.GetLlmSettings();
        RefreshProviderInfo();

        if (llm.Provider == LlmProvider.Gemini && string.IsNullOrWhiteSpace(llm.GeminiApiKey))
        {
            _notices.Show("Add your Gemini API key in Settings first.", NoticeKind.Error);
            _session.TryNavigate(AppSection.Settings);
            return;
        }

        if (!llm.IsLocal && !ReferenceEquals(_cloudConfirmedFor, schema))
        {
            int samples = schema.SampleData.Values.Sum(r => r.Count);
            string detail = samples > 0
                ? $"The schema of “{schema.DatabaseName}” and {samples} sample row(s) {(schema.SampleData.Count > 0 ? "(sensitive-looking values are masked when the privacy shield was on)" : "")} will be sent to Google Gemini."
                : $"The schema of “{schema.DatabaseName}” (no row data) will be sent to Google Gemini.";
            if (!await _dialogs.ConfirmAsync("Send data to the cloud?", detail + "\n\nContinue?", "Send", "Cancel")) return;
            _cloudConfirmedFor = schema;
        }

        _runCts?.Cancel();
        var cts = _runCts = new CancellationTokenSource();

        var previous = retry ? _lastReport : null;
        RunItems.Clear();
        foreach (var t in topics)
        {
            if (!(retry && t.Status == AnalysisStatus.Completed)) t.Status = AnalysisStatus.Pending;
            t.ErrorMessage = null;
            RunItems.Add(t);
        }

        IsRunning = true;
        HasRun = true;
        HasResult = false;
        CanRetry = false;
        Progress = 0;
        StatusText = "Preparing the analysis…";

        var request = new AnalysisRequest
        {
            Schema = schema,
            SchemaJson = _session.SchemaJson,
            Insights = _session.Insights,
            Topics = topics,
            Language = PromptBuilder.Languages[Math.Clamp(LanguageIndex, 0, PromptBuilder.Languages.Length - 1)]
        };

        var reporter = new Progress<AnalysisProgress>(OnProgress);

        try
        {
            var report = await _runner.RunAsync(request, llm, previous, reporter, cts.Token);
            if (retry && previous is not null) report.Id = previous.Id;
            await FinishAsync(report, cancelled: cts.IsCancellationRequested);
        }
        catch (Exception ex)
        {
            StatusText = "The analysis could not be completed.";
            ResultText = ex.GetBaseException().Message;
            HasResult = true;
            _notices.Show(ex.GetBaseException().Message, NoticeKind.Error);
        }
        finally
        {
            IsRunning = false;
            if (ReferenceEquals(_runCts, cts)) _runCts = null;
        }
    }

    private void OnProgress(AnalysisProgress p)
    {
        if (p.Topic is { } topic)
        {
            topic.Status = p.Status;
            topic.ErrorMessage = p.Error;
        }

        Progress = p.Total == 0 ? 0 : Math.Clamp(p.Completed / (double)p.Total, 0, 1);
        StatusText = p.Status switch
        {
            AnalysisStatus.Processing => $"Analyzing: {p.Message}",
            AnalysisStatus.Cancelled => "Cancelling…",
            _ => p.Message
        };
    }

    private async Task FinishAsync(AnalysisReport report, bool cancelled)
    {
        _lastReport = report;
        int ok = report.Sections.Count(s => !s.IsError);
        int failed = report.FailedSections;
        CanRetry = failed > 0 || RunItems.Any(t => t.Status is AnalysisStatus.Error or AnalysisStatus.Cancelled);

        if (report.Sections.Count == 0)
        {
            StatusText = cancelled ? "Cancelled — nothing to save." : "No results were produced.";
            ResultText = StatusText;
            HasResult = true;
            return;
        }

        await _reports.SaveAsync(report);
        string pdfNote = "";
        try
        {
            await _pdf.GenerateAsync(report, _reports.PdfPath(report.Id));
        }
        catch (Exception ex)
        {
            pdfNote = $" (PDF could not be created: {ex.GetBaseException().Message})";
        }

        _session.NotifyReportSaved(report.Id);
        Progress = 1;
        StatusText = cancelled ? "Cancelled — partial report saved." : "Analysis complete.";
        ResultText = $"{ok} section(s) completed" + (failed > 0 ? $", {failed} failed" : "") + $".{pdfNote}";
        HasResult = true;
        _notices.Show(cancelled ? "Partial report saved to Reports." : "Your report is ready.", failed > 0 ? NoticeKind.Warning : NoticeKind.Success);
    }

    [RelayCommand]
    private void Cancel()
    {
        if (!IsRunning) return;
        StatusText = "Cancelling…";
        _runCts?.Cancel();
    }

    [RelayCommand]
    private void OpenReport()
    {
        if (_lastReport is { } report) _session.OpenReport(report.Id);
    }

    [RelayCommand]
    private void NewRun()
    {
        if (IsRunning) return;
        HasRun = false;
        HasResult = false;
        RunItems.Clear();
        foreach (var t in Topics) t.Status = AnalysisStatus.Pending;
        _lastReport = null;
    }

    [RelayCommand]
    private void BackToInsights() => _session.TryNavigate(AppSection.Insights);
}
