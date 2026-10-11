using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AIDatabaseAnalyzer.Models;
using AIDatabaseAnalyzer.Services.Database;
using AIDatabaseAnalyzer.Services.Infrastructure;
using AIDatabaseAnalyzer.Services.Insights;

namespace AIDatabaseAnalyzer.ViewModels;

/// <summary>Step 2: pick the database, the tables and which parts of the schema to read.</summary>
public partial class ScopeViewModel : ObservableObject
{
    private readonly WorkspaceSession _session;
    private readonly IDbSchemaService _db;
    private readonly INoticeService _notices;
    private readonly List<TableSelection> _allTables = new();
    private CancellationTokenSource? _tablesCts;
    private CancellationTokenSource? _extractCts;

    public ScopeViewModel(WorkspaceSession session, IDbSchemaService db, ISettingsService settings, INoticeService notices)
    {
        _session = session;
        _db = db;
        _notices = notices;

        maskSampleData = settings.MaskSampleData;
        _session.ConnectionChanged += OnConnectionChanged;
        OnConnectionChanged();
    }

    public ObservableCollection<string> Databases { get; } = new();

    [ObservableProperty] private string? selectedDatabase;
    [ObservableProperty] private IReadOnlyList<TableSelection> filteredTables = Array.Empty<TableSelection>();
    [ObservableProperty] private string searchQuery = "";
    [ObservableProperty] private string selectedTableText = "No tables";

    [ObservableProperty] private bool includeViews = true;
    [ObservableProperty] private bool includeIndexes = true;
    [ObservableProperty] private bool includeTriggers = true;
    [ObservableProperty] private bool includeRoutines = true;
    [ObservableProperty] private bool includeConstraints = true;
    [ObservableProperty] private bool includeDefinitions = true;

    [NotifyPropertyChangedFor(nameof(ShowMaskOption))]
    [ObservableProperty] private bool includeSampleData;

    [ObservableProperty] private bool maskSampleData;

    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [ObservableProperty] private bool isLoadingTables;

    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [ObservableProperty] private bool isExtracting;

    [ObservableProperty] private string busyText = "";

    [NotifyPropertyChangedFor(nameof(HasError))]
    [ObservableProperty] private string? errorMessage;

    public bool ShowMaskOption => IncludeSampleData;
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool IsIdle => !IsLoadingTables && !IsExtracting;
    public bool HasTables => _allTables.Count > 0;
    public string ServerText => _session.Connection is { } c ? $"{c.Engine.DisplayName()} · {c.Server}" : "";

    // ------------------------------------------------------------------ session events

    private void OnConnectionChanged()
    {
        _tablesCts?.Cancel();
        Databases.Clear();
        foreach (string name in _session.Databases) Databases.Add(name);

        _allTables.Clear();
        FilteredTables = Array.Empty<TableSelection>();
        SelectedDatabase = null;
        ErrorMessage = null;
        UpdateCount();
        OnPropertyChanged(nameof(ServerText));
        OnPropertyChanged(nameof(HasTables));

        // a single database does not need a decision
        if (Databases.Count == 1) SelectedDatabase = Databases[0];
    }

    partial void OnSelectedDatabaseChanged(string? value)
    {
        _allTables.Clear();
        FilteredTables = Array.Empty<TableSelection>();
        UpdateCount();
        OnPropertyChanged(nameof(HasTables));
        if (!string.IsNullOrEmpty(value)) _ = LoadTablesAsync(value);
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    // ------------------------------------------------------------------ tables

    private async Task LoadTablesAsync(string database)
    {
        var connection = _session.Connection;
        if (connection is null) return;

        _tablesCts?.Cancel();
        var cts = _tablesCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        IsLoadingTables = true;
        BusyText = $"Reading tables of {database}…";
        ErrorMessage = null;

        try
        {
            var tables = await _db.GetTablesAsync(connection, database, cts.Token);
            if (cts.IsCancellationRequested || SelectedDatabase != database) return;

            foreach (var table in tables)
            {
                var item = new TableSelection
                {
                    Name = table.FullName,
                    IsSelected = true,
                    Detail = table.RowCount is { } rows ? $"≈ {rows:N0} rows" : ""
                };
                item.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(TableSelection.IsSelected)) UpdateCount();
                };
                _allTables.Add(item);
            }

            ApplyFilter();
            OnPropertyChanged(nameof(HasTables));
        }
        catch (OperationCanceledException)
        {
            // superseded by a newer selection (or timed out)
            if (cts == _tablesCts && SelectedDatabase == database) ErrorMessage = "Reading the table list timed out.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.GetBaseException().Message;
        }
        finally
        {
            if (cts == _tablesCts) IsLoadingTables = false;
        }
    }

    private void ApplyFilter()
    {
        string query = SearchQuery.Trim();
        FilteredTables = query.Length == 0
            ? _allTables.ToList()
            : _allTables.Where(t => t.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        UpdateCount();
    }

    private void UpdateCount()
    {
        int selected = _allTables.Count(t => t.IsSelected);
        SelectedTableText = _allTables.Count == 0
            ? "No tables"
            : FilteredTables.Count != _allTables.Count
                ? $"{selected} of {_allTables.Count} selected · {FilteredTables.Count} shown"
                : $"{selected} of {_allTables.Count} selected";
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var t in FilteredTables) t.IsSelected = true;
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var t in FilteredTables) t.IsSelected = false;
    }

    [RelayCommand]
    private void InvertSelection()
    {
        foreach (var t in FilteredTables) t.IsSelected = !t.IsSelected;
    }

    // ------------------------------------------------------------------ extraction

    [RelayCommand]
    private async Task ExtractAsync()
    {
        var connection = _session.Connection;
        if (connection is null || string.IsNullOrEmpty(SelectedDatabase) || IsExtracting) return;

        var selected = _allTables.Where(t => t.IsSelected).Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selected.Count == 0)
        {
            ErrorMessage = "Select at least one table to analyze.";
            return;
        }

        var options = new SchemaExtractionOptions
        {
            SelectedTables = selected,
            IncludeViews = IncludeViews,
            IncludeIndexes = IncludeIndexes,
            IncludeTriggers = IncludeTriggers,
            IncludeRoutines = IncludeRoutines,
            IncludeConstraints = IncludeConstraints,
            IncludeDefinitions = IncludeDefinitions,
            IncludeSampleData = IncludeSampleData,
            MaskSampleData = MaskSampleData
        };

        _extractCts?.Cancel();
        var cts = _extractCts = new CancellationTokenSource();
        IsExtracting = true;
        ErrorMessage = null;
        BusyText = "Connecting…";

        try
        {
            var progress = new Progress<string>(message => BusyText = message);
            var schema = await _db.ExtractSchemaAsync(connection, SelectedDatabase, options, progress, cts.Token);

            BusyText = "Analyzing the schema…";
            var (json, insights) = await Task.Run(() => (SchemaSerializer.ToJson(schema, false), SchemaInsightsService.Analyze(schema)), cts.Token);

            _session.SetSchema(schema, json, insights);
            if (schema.Warnings.Count > 0)
                _notices.Show($"Schema extracted with {schema.Warnings.Count} warning(s) — see the Insights page.", NoticeKind.Warning);
            else
                _notices.Show($"Extracted {schema.Tables.Count} tables, {schema.ForeignKeys.Count} relationships.", NoticeKind.Success);

            _session.TryNavigate(AppSection.Insights);
        }
        catch (OperationCanceledException)
        {
            _notices.Show("Extraction cancelled.", NoticeKind.Info);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.GetBaseException().Message;
        }
        finally
        {
            IsExtracting = false;
        }
    }

    [RelayCommand]
    private void CancelExtraction() => _extractCts?.Cancel();

    [RelayCommand]
    private void Back() => _session.TryNavigate(AppSection.Connect);
}
