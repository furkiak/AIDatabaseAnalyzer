using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AIDatabaseAnalyzer.Models;
using AIDatabaseAnalyzer.Services.Infrastructure;
using AIDatabaseAnalyzer.Services.Reporting;

namespace AIDatabaseAnalyzer.ViewModels;

/// <summary>The report history and the in-app report viewer with PDF / Markdown / HTML export.</summary>
public partial class ReportsViewModel : ObservableObject
{
    private readonly WorkspaceSession _session;
    private readonly IReportStore _store;
    private readonly IFileExportService _files;
    private readonly IPdfReportService _pdf;
    private readonly IThemeService _theme;
    private readonly INoticeService _notices;
    private readonly IDialogService _dialogs;

    private AnalysisReport? _current;

    public ReportsViewModel(
        WorkspaceSession session,
        IReportStore store,
        IFileExportService files,
        IPdfReportService pdf,
        IThemeService theme,
        INoticeService notices,
        IDialogService dialogs)
    {
        _session = session;
        _store = store;
        _files = files;
        _pdf = pdf;
        _theme = theme;
        _notices = notices;
        _dialogs = dialogs;

        _session.ReportSaved += reportId => { _ = RefreshAsync(); };
        _session.ReportOpenRequested += (id, section) => _ = OpenByIdAsync(id, section);
        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceSession.CurrentSection) && _session.CurrentSection == AppSection.Reports)
                _ = RefreshAsync();
        };
        _ = RefreshAsync();
    }

    public ObservableCollection<ReportItem> Items { get; } = new();

    [NotifyPropertyChangedFor(nameof(IsListVisible))]
    [ObservableProperty] private bool isViewing;

    [ObservableProperty] private string viewerHtml = "";
    [ObservableProperty] private string viewerTitle = "";
    [NotifyPropertyChangedFor(nameof(IsEmptyVisible))]
    [ObservableProperty] private bool hasItems;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string folderText = "";

    /// <summary>The open report belongs to the loaded database: table names link to the diagram.</summary>
    [ObservableProperty] private bool isLinked;

    public bool IsListVisible => !IsViewing;
    public bool IsEmptyVisible => !HasItems;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            var summaries = await _store.ListAsync();
            Items.Clear();
            foreach (var summary in summaries) Items.Add(new ReportItem(summary, item => _ = OpenByIdAsync(item.Id), item => _ = DeleteAsync(item)));
            HasItems = Items.Count > 0;
            FolderText = _store.Folder;
            _session.ReportCount = Items.Count;
        }
        catch (Exception ex)
        {
            _notices.Show("The report history could not be read: " + ex.GetBaseException().Message, NoticeKind.Error);
        }
    }

    private async Task OpenByIdAsync(string id, int? section = null)
    {
        IsBusy = true;
        try
        {
            var report = await _store.LoadAsync(id);
            if (report is null)
            {
                _notices.Show("That report could not be opened (file missing or damaged).", NoticeKind.Error);
                await RefreshAsync();
                return;
            }

            _current = report;
            ViewerTitle = $"{report.DatabaseName} · {report.GeneratedAtUtc.ToLocalTime():g}";
            string theme = _theme.EffectiveTheme;

            // When the report belongs to the loaded schema, table names become links into the ER diagram.
            var schema = _session.Schema is { } loaded && loaded.DatabaseName == report.DatabaseName ? loaded : null;
            if (schema is not null && (_session.LatestReport is null || _session.LatestReport.GeneratedAtUtc <= report.GeneratedAtUtc))
                _session.LatestReport = report;
            Func<string, string?> link = token =>
                schema is not null && SchemaLinks.ResolveTable(token, schema) is { } table ? "aida://table/" + Uri.EscapeDataString(table) : null;

            ViewerHtml = await Task.Run(() => ReportRenderer.ToHtml(report, theme, link, section));
            IsLinked = schema is not null;
            IsViewing = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteAsync(ReportItem item)
    {
        if (!await _dialogs.ConfirmAsync("Delete report", $"Delete the report for “{item.Title}” from {item.DateText}?", "Delete")) return;
        await _store.DeleteAsync(item.Id);
        await RefreshAsync();
        _notices.Show("Report deleted.", NoticeKind.Info);
    }

    /// <summary>Handles "aida://" links of the report page.</summary>
    public async Task HandleLinkAsync(string command, string argument)
    {
        switch (command)
        {
            case "table":
                if (!_session.ShowInDiagram(argument))
                    _notices.Show("Connect to this database and extract its schema to open the diagram.", NoticeKind.Info);
                break;
            case "copy":
                await Clipboard.Default.SetTextAsync(argument);
                _notices.Show("Copied to the clipboard.", NoticeKind.Success);
                break;
        }
    }

    [RelayCommand]
    private void OpenDiagram() => _session.ShowInDiagram();

    [RelayCommand]
    private void CloseViewer()
    {
        IsViewing = false;
        ViewerHtml = "";
        _current = null;
    }

    [RelayCommand]
    private async Task ExportPdfAsync()
    {
        if (_current is not { } report) return;
        try
        {
            string path = _store.PdfPath(report.Id);
            if (!File.Exists(path)) await _pdf.GenerateAsync(report, path);
            string? saved = await _files.SaveCopyAsync(path, FileName(report, "pdf"));
            if (saved is not null) _notices.Show("PDF saved.", NoticeKind.Success);
        }
        catch (Exception ex)
        {
            _notices.Show("The PDF could not be created: " + ex.GetBaseException().Message, NoticeKind.Error);
        }
    }

    [RelayCommand]
    private async Task ExportMarkdownAsync()
    {
        if (_current is not { } report) return;
        string? saved = await _files.SaveTextAsync(FileName(report, "md"), ReportRenderer.ToMarkdown(report));
        if (saved is not null) _notices.Show("Markdown saved.", NoticeKind.Success);
    }

    [RelayCommand]
    private async Task ExportHtmlAsync()
    {
        if (_current is not { } report) return;
        string? saved = await _files.SaveTextAsync(FileName(report, "html"), ReportRenderer.ToHtml(report));
        if (saved is not null) _notices.Show("HTML saved.", NoticeKind.Success);
    }

    [RelayCommand]
    private async Task CopyMarkdownAsync()
    {
        if (_current is not { } report) return;
        await Clipboard.Default.SetTextAsync(ReportRenderer.ToMarkdown(report));
        _notices.Show("Report copied as Markdown.", NoticeKind.Success);
    }

    [RelayCommand]
    private Task OpenFolderAsync() => _files.OpenFolderAsync(_store.Folder);

    private static string FileName(AnalysisReport report, string extension)
    {
        string safe = string.Concat(report.DatabaseName.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return $"{safe}_{report.GeneratedAtUtc.ToLocalTime():yyyyMMdd-HHmm}_analysis.{extension}";
    }
}
