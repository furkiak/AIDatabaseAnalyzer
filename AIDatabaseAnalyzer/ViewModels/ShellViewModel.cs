using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AIDatabaseAnalyzer.Services.Infrastructure;

namespace AIDatabaseAnalyzer.ViewModels;

/// <summary>An entry of the sidebar.</summary>
public partial class NavItem : ObservableObject
{
    private readonly Action<AppSection> _navigate;

    public NavItem(AppSection section, string title, string icon, Action<AppSection> navigate)
    {
        Section = section;
        Title = title;
        _iconName = icon;
        _navigate = navigate;
    }

    private readonly string _iconName;

    public AppSection Section { get; }
    public string Title { get; }

    [NotifyPropertyChangedFor(nameof(Icon))]
    [ObservableProperty] private bool isActive;

    [ObservableProperty] private bool isEnabled = true;
    [ObservableProperty] private string caption = "";

    /// <summary>White icon on the highlighted item, grey otherwise.</summary>
    public string Icon => IsActive ? $"{_iconName}_on.png" : $"{_iconName}.png";

    [RelayCommand]
    private void Select() => _navigate(Section);
}

/// <summary>Drives the window chrome: sidebar navigation, section visibility and toast notifications.</summary>
public partial class ShellViewModel : ObservableObject
{
    private readonly WorkspaceSession _session;
    private CancellationTokenSource? _noticeCts;

    [ObservableProperty] private string noticeText = "";
    [ObservableProperty] private bool isNoticeVisible;
    [ObservableProperty] private bool isNoticeError;
    [ObservableProperty] private bool isNoticeSuccess;
    [ObservableProperty] private bool isNoticeWarning;

    public ShellViewModel(WorkspaceSession session, INoticeService notices)
    {
        _session = session;

        Connect = new NavItem(AppSection.Connect, "Connect", "nav_connect", Navigate);
        Scope = new NavItem(AppSection.Scope, "Scope", "nav_scope", Navigate);
        Insights = new NavItem(AppSection.Insights, "Insights", "nav_insights", Navigate);
        Analysis = new NavItem(AppSection.Analysis, "AI Analysis", "nav_analysis", Navigate);
        ReportsItem = new NavItem(AppSection.Reports, "Reports", "nav_reports", Navigate);
        SettingsItem = new NavItem(AppSection.Settings, "Settings", "nav_settings", Navigate);

        WorkflowItems = new[] { Connect, Scope, Insights, Analysis };
        LibraryItems = new[] { ReportsItem, SettingsItem };

        _session.PropertyChanged += (_, _) => Refresh();
        _session.SchemaChanged += Refresh;
        _session.ConnectionChanged += Refresh;
        notices.NoticeRaised += OnNotice;
        Refresh();
    }

    public NavItem Connect { get; }
    public NavItem Scope { get; }
    public NavItem Insights { get; }
    public NavItem Analysis { get; }
    public NavItem ReportsItem { get; }
    public NavItem SettingsItem { get; }

    public IReadOnlyList<NavItem> WorkflowItems { get; }
    public IReadOnlyList<NavItem> LibraryItems { get; }

    public AppSection Current => _session.CurrentSection;

    public bool IsConnectVisible => Current == AppSection.Connect;
    public bool IsScopeVisible => Current == AppSection.Scope;
    public bool IsInsightsVisible => Current == AppSection.Insights;
    public bool IsAnalysisVisible => Current == AppSection.Analysis;
    public bool IsReportsVisible => Current == AppSection.Reports;
    public bool IsSettingsVisible => Current == AppSection.Settings;
    public bool IsDiagramVisible => Current == AppSection.Diagram;

    private void Navigate(AppSection section) => _session.TryNavigate(section);

    private void Refresh()
    {
        var current = _session.CurrentSection;
        foreach (var item in WorkflowItems.Concat(LibraryItems))
            item.IsActive = item.Section == current || item.Section == AppSection.Insights && current == AppSection.Diagram;

        Scope.IsEnabled = _session.HasConnection;
        Insights.IsEnabled = _session.HasSchema;
        Analysis.IsEnabled = _session.HasSchema;

        ReportsItem.Caption = _session.ReportCount == 0 ? "" : _session.ReportCount.ToString();
        Connect.Caption = _session.Connection is { } c ? Shorten(c.Server, 16) : "";
        Scope.Caption = _session.Schema is { } s ? $"{s.Tables.Count} tables" : _session.HasConnection ? $"{_session.Databases.Count} DBs" : "";
        Insights.Caption = _session.Insights is { } i ? $"{i.Score}/100" : "";

        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(IsConnectVisible));
        OnPropertyChanged(nameof(IsScopeVisible));
        OnPropertyChanged(nameof(IsInsightsVisible));
        OnPropertyChanged(nameof(IsAnalysisVisible));
        OnPropertyChanged(nameof(IsReportsVisible));
        OnPropertyChanged(nameof(IsSettingsVisible));
        OnPropertyChanged(nameof(IsDiagramVisible));
    }

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    private void OnNotice(string message, NoticeKind kind)
    {
        _noticeCts?.Cancel();
        var cts = _noticeCts = new CancellationTokenSource();

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            NoticeText = message;
            IsNoticeError = kind == NoticeKind.Error;
            IsNoticeSuccess = kind == NoticeKind.Success;
            IsNoticeWarning = kind == NoticeKind.Warning;
            IsNoticeVisible = true;

            try
            {
                await Task.Delay(kind == NoticeKind.Error ? 7000 : 4200, cts.Token);
                IsNoticeVisible = false;
            }
            catch (TaskCanceledException)
            {
                // a newer notice replaced this one
            }
        });
    }

    [RelayCommand]
    private void DismissNotice() => IsNoticeVisible = false;
}
