using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AIDatabaseAnalyzer.Controls;
using AIDatabaseAnalyzer.Models;
using AIDatabaseAnalyzer.Services.History;
using AIDatabaseAnalyzer.Services.Infrastructure;
using AIDatabaseAnalyzer.Services.Insights;
using AIDatabaseAnalyzer.Services.Reporting;

namespace AIDatabaseAnalyzer.ViewModels;

/// <summary>A saved schema version in the list.</summary>
public partial class SnapshotItem : ObservableObject
{
    private readonly Action<SnapshotItem, string> _act;

    public SnapshotItem(SnapshotSummary summary, bool isCurrentDatabase, bool canCompareLive, Action<SnapshotItem, string> act)
    {
        Summary = summary;
        IsCurrentDatabase = isCurrentDatabase;
        CanCompareLive = canCompareLive;
        _act = act;
    }

    public SnapshotSummary Summary { get; }
    public bool IsCurrentDatabase { get; }
    public bool CanCompareLive { get; }

    public string Title => string.IsNullOrEmpty(Summary.Label) ? Summary.TakenAtUtc.ToLocalTime().ToString("g") : Summary.Label;
    public string Subtitle =>
        $"{Summary.DatabaseName} · {((DbEngine)Summary.Engine).DisplayName()} · {Summary.Server}" +
        (string.IsNullOrEmpty(Summary.Label) ? "" : " · " + Summary.TakenAtUtc.ToLocalTime().ToString("g"));
    public string Details => $"{Summary.TableCount} tables · {Summary.ColumnCount} columns" + (Summary.IsPartial ? " · partial" : "");
    public string ScoreText => Summary.HealthScore > 0 ? Summary.HealthScore.ToString() : "–";
    public Color ScoreColor => Summary.HealthScore switch
    {
        >= 80 => Color.FromArgb("#1F9D55"),
        >= 55 => Color.FromArgb("#D98A00"),
        > 0 => Color.FromArgb("#D92D20"),
        _ => Color.FromArgb("#8E8E93")
    };

    [RelayCommand] private void CompareLive() => _act(this, "live");
    [RelayCommand] private void ComparePrevious() => _act(this, "previous");
    [RelayCommand] private void Delete() => _act(this, "delete");
}

/// <summary>
/// Schema history: every extraction is saved as a snapshot (when its structure changed); any version can be compared
/// with the live schema or its predecessor, and the health score is followed over time.
/// </summary>
public partial class SnapshotsViewModel : ObservableObject
{
    private readonly WorkspaceSession _session;
    private readonly SnapshotStore _store;
    private readonly IThemeService _theme;
    private readonly INoticeService _notices;
    private readonly IDialogService _dialogs;
    private readonly IFileExportService _files;
    private IReadOnlyList<SnapshotSummary> _all = Array.Empty<SnapshotSummary>();
    private string _diffMarkdown = "";

    public SnapshotsViewModel(WorkspaceSession session, SnapshotStore store, IThemeService theme, INoticeService notices, IDialogService dialogs, IFileExportService files)
    {
        _session = session;
        _store = store;
        _theme = theme;
        _notices = notices;
        _dialogs = dialogs;
        _files = files;

        _session.SchemaChanged += () => _ = OnSchemaExtractedAsync();
        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceSession.CurrentSection) && _session.CurrentSection == AppSection.Snapshots)
                _ = RefreshAsync();
        };
        _ = RefreshAsync();
    }

    public ObservableCollection<SnapshotItem> Items { get; } = new();

    [ObservableProperty] private IReadOnlyList<TrendPoint> trend = Array.Empty<TrendPoint>();
    [ObservableProperty] private bool hasTrend;
    [ObservableProperty] private string trendText = "";
    [ObservableProperty] private bool hasItems;
    [ObservableProperty] private bool isEmpty = true;
    [ObservableProperty] private bool hasSchema;
    [ObservableProperty] private string snapshotLabel = "";

    [NotifyPropertyChangedFor(nameof(IsListVisible))]
    [ObservableProperty] private bool hasDiff;

    public bool IsListVisible => !HasDiff;
    [ObservableProperty] private string diffTitle = "";
    [ObservableProperty] private string diffSummary = "";
    [ObservableProperty] private string diffHtml = "";

    public string FolderText => _store.Folder;

    private string CurrentIdentity =>
        _session.Schema is { } schema && _session.Connection is { } connection
            ? SnapshotStore.Identity(schema.Engine, connection.Server, schema.DatabaseName)
            : "";

    /// <summary>Saves a snapshot after every extraction and tells the user when the structure drifted since the last one.</summary>
    private async Task OnSchemaExtractedAsync()
    {
        if (_session.Schema is not { } schema || _session.Connection is not { } connection) return;
        try
        {
            var previous = schema.IsPartial ? null : await _store.LatestAsync(schema.Engine, connection.Server, schema.DatabaseName);
            if (previous is not null && await _store.LoadAsync(previous.Id) is { } before)
            {
                var diff = SchemaDiff.Compare(before, schema);
                _session.DriftSummary = diff.IsEmpty ? null : $"Since {previous.TakenAtUtc.ToLocalTime():g}: {diff.Summary}.";
            }
            else
            {
                _session.DriftSummary = null;
            }

            await _store.SaveAsync(schema, connection.Server, _session.Insights);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _notices.Show("The schema snapshot could not be saved: " + ex.GetBaseException().Message, NoticeKind.Warning);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        _all = await _store.ListAsync();
        string identity = CurrentIdentity;
        HasSchema = _session.Schema is not null;

        Items.Clear();
        foreach (var summary in _all.OrderByDescending(s => s.Identity == identity).ThenByDescending(s => s.TakenAtUtc))
        {
            bool current = identity.Length > 0 && summary.Identity == identity;
            Items.Add(new SnapshotItem(summary, current, current && HasSchema, (item, action) => _ = ActAsync(item, action)));
        }
        HasItems = Items.Count > 0;
        IsEmpty = !HasItems;

        // health over time for the database that is open now (or the most recent one)
        string trendIdentity = identity.Length > 0 ? identity : _all.FirstOrDefault()?.Identity ?? "";
        var history = _all.Where(s => s.Identity == trendIdentity && s.HealthScore > 0 && !s.IsPartial).OrderBy(s => s.TakenAtUtc).TakeLast(20).ToList();
        Trend = history.Select(s => new TrendPoint(s.HealthScore, s.TakenAtUtc.ToLocalTime().ToString("d MMM"))).ToList();
        HasTrend = history.Count >= 2;
        TrendText = HasTrend
            ? $"{history[0].DatabaseName}: {history[0].HealthScore} → {history[^1].HealthScore} over {history.Count} snapshots"
            : "";
    }

    [RelayCommand]
    private async Task TakeSnapshotAsync()
    {
        if (_session.Schema is not { } schema || _session.Connection is not { } connection) return;
        var saved = await _store.SaveAsync(schema, connection.Server, _session.Insights, SnapshotLabel, skipIfUnchanged: false);
        SnapshotLabel = "";
        await RefreshAsync();
        _notices.Show($"Snapshot of {schema.DatabaseName} saved ({saved.TableCount} tables).", NoticeKind.Success);
    }

    private async Task ActAsync(SnapshotItem item, string action)
    {
        switch (action)
        {
            case "delete":
                if (!await _dialogs.ConfirmAsync("Delete snapshot", $"Delete the snapshot “{item.Title}” of {item.Summary.DatabaseName}?", "Delete")) return;
                await _store.DeleteAsync(item.Summary.Id);
                await RefreshAsync();
                break;

            case "live":
                if (_session.Schema is not { } live) return;
                if (await _store.LoadAsync(item.Summary.Id) is not { } before)
                {
                    _notices.Show("That snapshot could not be read.", NoticeKind.Error);
                    return;
                }
                ShowDiff(SchemaDiff.Compare(before, live), item.Title, "live schema");
                break;

            case "previous":
                var older = _all.Where(s => s.Identity == item.Summary.Identity && s.TakenAtUtc < item.Summary.TakenAtUtc)
                                .OrderByDescending(s => s.TakenAtUtc).FirstOrDefault();
                if (older is null)
                {
                    _notices.Show("This is the oldest snapshot of the database.", NoticeKind.Info);
                    return;
                }
                if (await _store.LoadAsync(older.Id) is not { } a || await _store.LoadAsync(item.Summary.Id) is not { } b)
                {
                    _notices.Show("A snapshot could not be read.", NoticeKind.Error);
                    return;
                }
                ShowDiff(SchemaDiff.Compare(a, b), older.TakenAtUtc.ToLocalTime().ToString("g"), item.Title);
                break;
        }
    }

    private void ShowDiff(SchemaDiff diff, string from, string to)
    {
        DiffTitle = $"{from} → {to}";
        DiffSummary = diff.Summary;
        _diffMarkdown = diff.ToMarkdown(from, to);

        var schema = _session.Schema;
        Func<string, string?>? link = schema is null
            ? null
            : token => SchemaLinks.ResolveTable(token, schema) is { } table ? "aida://table/" + Uri.EscapeDataString(table) : null;
        DiffHtml = MarkdownPage.ToHtml(_diffMarkdown, _theme.EffectiveTheme, link);
        HasDiff = true;
    }

    [RelayCommand]
    private void CloseDiff()
    {
        HasDiff = false;
        DiffHtml = "";
    }

    [RelayCommand]
    private async Task CopyDiffAsync()
    {
        if (_diffMarkdown.Length == 0) return;
        await Clipboard.Default.SetTextAsync(_diffMarkdown);
        _notices.Show("Changes copied as Markdown.", NoticeKind.Success);
    }

    [RelayCommand]
    private async Task SaveDiffAsync()
    {
        if (_diffMarkdown.Length == 0) return;
        string? path = await _files.SaveTextAsync("schema-changes.md", _diffMarkdown);
        if (path is not null) _notices.Show("Changes saved.", NoticeKind.Success);
    }

    [RelayCommand]
    private void ReviewWithAi()
    {
        if (_diffMarkdown.Length == 0) return;
        if (!_session.Ask("Review these schema changes like a senior DBA reviewing a migration: what can break applications or lose data, " +
                          "what needs a data migration or a backfill, which locks or rewrites to expect on large tables, and what is missing (indexes, constraints).\n\n" + _diffMarkdown))
            _notices.Show("Extract a schema first: the review uses it as context.", NoticeKind.Info);
    }

    /// <summary>Handles "aida://" links of the changes page.</summary>
    public Task HandleLinkAsync(string command, string argument)
    {
        if (command == "table") _session.ShowInDiagram(argument);
        return Task.CompletedTask;
    }
}
