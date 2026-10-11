using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AIDatabaseAnalyzer.Models;
using AIDatabaseAnalyzer.Services.Database;
using AIDatabaseAnalyzer.Services.Infrastructure;
using AIDatabaseAnalyzer.Services.Insights;
using AIDatabaseAnalyzer.Services.Llm;
using AIDatabaseAnalyzer.Services.Reporting;

namespace AIDatabaseAnalyzer.ViewModels;

/// <summary>Step 3: instant, offline review of the extracted schema plus exports and the ER diagram.</summary>
public partial class InsightsViewModel : ObservableObject
{
    private const int MaxPreviewChars = 40_000;

    private readonly WorkspaceSession _session;
    private readonly ISettingsService _settings;
    private readonly IFileExportService _files;
    private readonly INoticeService _notices;
    private readonly List<FindingItem> _allFindings = new();

    public InsightsViewModel(WorkspaceSession session, ISettingsService settings, IFileExportService files, INoticeService notices)
    {
        _session = session;
        _settings = settings;
        _files = files;
        _notices = notices;
        _session.SchemaChanged += Rebuild;
        Rebuild();
    }

    public ObservableCollection<FindingItem> Findings { get; } = new();
    public ObservableCollection<FilterChip> SeverityChips { get; } = new();
    public ObservableCollection<string> Warnings { get; } = new();

    [ObservableProperty] private string databaseTitle = "";
    [ObservableProperty] private string subtitle = "";
    [ObservableProperty] private string tablesText = "–";
    [ObservableProperty] private string columnsText = "–";
    [ObservableProperty] private string relationsText = "–";
    [ObservableProperty] private string indexesText = "–";
    [ObservableProperty] private double score;
    [ObservableProperty] private string grade = "";
    [ObservableProperty] private string headline = "";
    [ObservableProperty] private string tokenText = "";
    [NotifyPropertyChangedFor(nameof(HasTokenIssue))]
    [NotifyPropertyChangedFor(nameof(IsTokenOk))]
    [ObservableProperty] private bool isTokenWarning;

    [NotifyPropertyChangedFor(nameof(HasTokenIssue))]
    [NotifyPropertyChangedFor(nameof(IsTokenOk))]
    [NotifyPropertyChangedFor(nameof(IsTokenWarningOnly))]
    [ObservableProperty] private bool isTokenDanger;
    [ObservableProperty] private string schemaPreview = "";
    [ObservableProperty] private bool isPreviewVisible;
    [ObservableProperty] private bool hasFindings;
    [ObservableProperty] private bool noFindings;
    [ObservableProperty] private bool hasWarnings;
    [ObservableProperty] private bool hasSkipped;
    [ObservableProperty] private string skippedText = "";

    public bool HasTokenIssue => IsTokenWarning || IsTokenDanger;
    public bool IsTokenOk => !HasTokenIssue;
    public bool IsTokenWarningOnly => IsTokenWarning && !IsTokenDanger;

    private void Rebuild()
    {
        var schema = _session.Schema;
        var insights = _session.Insights;

        _allFindings.Clear();
        Findings.Clear();
        SeverityChips.Clear();
        Warnings.Clear();

        if (schema is null || insights is null)
        {
            DatabaseTitle = "No schema yet";
            Subtitle = "Connect to a server and extract a schema first.";
            Score = 0;
            return;
        }

        DatabaseTitle = schema.DatabaseName;
        Subtitle = $"{schema.Engine.DisplayName()}{(string.IsNullOrWhiteSpace(schema.ServerVersion) ? "" : " " + FirstToken(schema.ServerVersion))} · extracted {schema.ExtractedAtUtc.ToLocalTime():HH:mm}";
        TablesText = schema.Tables.Count.ToString("N0");
        ColumnsText = schema.ColumnCount.ToString("N0");
        RelationsText = schema.ForeignKeys.Count.ToString("N0");
        IndexesText = schema.IndexesIncluded ? schema.Indexes.Count.ToString("N0") : "–";

        Score = insights.Score;
        Grade = insights.Grade;
        Headline = insights.Headline;

        foreach (var finding in insights.Findings) _allFindings.Add(new FindingItem(finding));
        HasFindings = _allFindings.Count > 0;
        NoFindings = !HasFindings;

        AddChip("all", "All", _allFindings.Count, selected: true);
        foreach (var severity in new[] { FindingSeverity.Critical, FindingSeverity.High, FindingSeverity.Medium, FindingSeverity.Low, FindingSeverity.Info })
        {
            int n = _allFindings.Count(f => f.Finding.Severity == severity);
            if (n > 0) AddChip(severity.ToString(), severity.ToString(), n, selected: false);
        }
        ApplyFilter("all");

        foreach (string warning in schema.Warnings) Warnings.Add(warning);
        HasWarnings = Warnings.Count > 0;
        HasSkipped = insights.SkippedChecks.Count > 0;
        SkippedText = string.Join(" ", insights.SkippedChecks);

        UpdateTokenInfo();
        BuildPreview(schema);
    }

    private void AddChip(string key, string label, int count, bool selected) =>
        SeverityChips.Add(new FilterChip(key, label, chip => ApplyFilter(chip.Key)) { Count = count, IsSelected = selected });

    private void ApplyFilter(string key)
    {
        foreach (var chip in SeverityChips) chip.IsSelected = chip.Key == key;
        Findings.Clear();
        foreach (var item in _allFindings.Where(f => key == "all" || f.Finding.Severity.ToString() == key))
            Findings.Add(item);
    }

    private void UpdateTokenInfo()
    {
        int tokens = SchemaSerializer.EstimateTokens(_session.SchemaJson);
        var llm = _settings.GetLlmSettings();
        int available = llm.ContextTokens;

        string size = $"{_session.SchemaJson.Length / 1024.0:N1} KB · ≈{tokens:N0} tokens per request";
        double usage = tokens / (double)available;
        IsTokenDanger = usage > 0.8;
        IsTokenWarning = !IsTokenDanger && usage > 0.4;

        TokenText = IsTokenDanger
            ? $"{size} — too large for {llm.ModelName} ({available:N0} token window). Select fewer tables or disable sample data / definitions."
            : IsTokenWarning
                ? $"{size} — large for the {available:N0} token window of {llm.ModelName}."
                : size;
    }

    private void BuildPreview(DatabaseSchema schema)
    {
        string pretty = SchemaSerializer.ToJson(schema, indented: true);
        SchemaPreview = pretty.Length > MaxPreviewChars
            ? pretty[..MaxPreviewChars] + $"\n\n… preview truncated ({pretty.Length - MaxPreviewChars:N0} more characters). Use Export or Copy for the full JSON."
            : pretty;
    }

    private static string FirstToken(string version)
    {
        int space = version.IndexOf(' ');
        return space > 0 ? version[..space] : version;
    }

    // ------------------------------------------------------------------ commands

    [RelayCommand]
    private void TogglePreview() => IsPreviewVisible = !IsPreviewVisible;

    [RelayCommand]
    private void Visualize() => _session.TryNavigate(AppSection.Diagram);

    [RelayCommand]
    private void Continue() => _session.TryNavigate(AppSection.Analysis);

    [RelayCommand]
    private void BackToScope() => _session.TryNavigate(AppSection.Scope);

    [RelayCommand]
    private async Task ExportJsonAsync()
    {
        if (_session.Schema is not { } schema) return;
        string json = SchemaSerializer.ToJson(schema, indented: true);
        string? path = await _files.SaveTextAsync($"{schema.DatabaseName}-schema.json", json);
        if (path is not null) _notices.Show($"Schema saved to {Path.GetFileName(path)}", NoticeKind.Success);
    }

    [RelayCommand]
    private async Task ExportDictionaryAsync()
    {
        if (_session.Schema is not { } schema) return;
        string markdown = SchemaSerializer.ToMarkdownDictionary(schema);
        string? path = await _files.SaveTextAsync($"{schema.DatabaseName}-data-dictionary.md", markdown);
        if (path is not null) _notices.Show($"Data dictionary saved to {Path.GetFileName(path)}", NoticeKind.Success);
    }

    [RelayCommand]
    private async Task ExportDiagramAsync()
    {
        if (_session.Schema is not { } schema) return;
        string html = await Task.Run(() => DiagramHtmlBuilder.Build(schema, "auto", embedded: false));
        string? path = await _files.SaveTextAsync($"{schema.DatabaseName}-er-diagram.html", html);
        if (path is not null) _notices.Show($"Interactive diagram saved to {Path.GetFileName(path)}", NoticeKind.Success);
    }

    [RelayCommand]
    private async Task CopyJsonAsync()
    {
        if (string.IsNullOrEmpty(_session.SchemaJson)) return;
        await Clipboard.Default.SetTextAsync(_session.SchemaJson);
        _notices.Show("Compact schema JSON copied to the clipboard.", NoticeKind.Success);
    }

    [RelayCommand]
    private void StartOver() => _session.Reset();
}
