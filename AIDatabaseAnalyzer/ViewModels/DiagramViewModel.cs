using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AIDatabaseAnalyzer.Services.Infrastructure;
using AIDatabaseAnalyzer.Services.Reporting;

namespace AIDatabaseAnalyzer.ViewModels;

/// <summary>Hosts the interactive ER diagram. The page itself runs inside a WebView.</summary>
public partial class DiagramViewModel : ObservableObject
{
    /// <summary>The WebView limits inline HTML to about 2 MB; stay below it.</summary>
    private const int MaxHtmlChars = 1_800_000;

    private readonly WorkspaceSession _session;
    private readonly IFileExportService _files;
    private readonly IThemeService _theme;
    private readonly INoticeService _notices;

    public DiagramViewModel(WorkspaceSession session, IFileExportService files, IThemeService theme, INoticeService notices)
    {
        _session = session;
        _files = files;
        _theme = theme;
        _notices = notices;

        _session.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName != nameof(WorkspaceSession.CurrentSection)) return;
            if (_session.CurrentSection == AppSection.Diagram) await BuildAsync();
            else if (_session.CurrentSection != AppSection.Diagram && HtmlSource.Length > 0) Release();
        };
    }

    [ObservableProperty] private string htmlSource = "";
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string title = "ER Diagram";

    /// <summary>Set by the view: returns the diagram as Base64-encoded SVG (calls into the WebView).</summary>
    public Func<Task<string?>>? SvgProvider { get; set; }

    private async Task BuildAsync()
    {
        var schema = _session.Schema;
        if (schema is null) return;

        Title = $"{schema.DatabaseName} · ER diagram";
        string theme = _theme.EffectiveTheme;
        IsLoading = true;
        try
        {
            string html = await Task.Run(() =>
            {
                string page = DiagramHtmlBuilder.Build(schema, theme);
                // very large schemas: drop the sample rows from the page to stay below the WebView limit
                return page.Length > MaxHtmlChars ? DiagramHtmlBuilder.Build(schema, theme, includeSamples: false) : page;
            });
            HtmlSource = html;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Release()
    {
        HtmlSource = "";
    }

    [RelayCommand]
    private void Close() => _session.TryNavigate(AppSection.Insights);

    [RelayCommand]
    private async Task ExportSvgAsync()
    {
        if (SvgProvider is null || _session.Schema is not { } schema) return;
        try
        {
            string? base64 = await SvgProvider();
            if (string.IsNullOrWhiteSpace(base64))
            {
                _notices.Show("The diagram is not ready yet.", NoticeKind.Warning);
                return;
            }

            byte[] bytes = Convert.FromBase64String(base64.Trim().Trim('"'));
            string? path = await _files.SaveAsync($"{schema.DatabaseName}-er-diagram.svg", bytes);
            if (path is not null) _notices.Show("SVG saved.", NoticeKind.Success);
        }
        catch (Exception ex)
        {
            _notices.Show("SVG export failed: " + ex.GetBaseException().Message, NoticeKind.Error);
        }
    }

    [RelayCommand]
    private async Task ExportHtmlAsync()
    {
        if (_session.Schema is not { } schema) return;
        string html = await Task.Run(() => DiagramHtmlBuilder.Build(schema, "auto", embedded: false));
        string? path = await _files.SaveTextAsync($"{schema.DatabaseName}-er-diagram.html", html);
        if (path is not null) _notices.Show("Interactive diagram saved — open it in any browser.", NoticeKind.Success);
    }
}
