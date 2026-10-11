using CommunityToolkit.Mvvm.ComponentModel;
using AIDatabaseAnalyzer.Models;
using AIDatabaseAnalyzer.Services.Insights;

namespace AIDatabaseAnalyzer.ViewModels;

public enum AppSection
{
    Connect,
    Scope,
    Insights,
    Analysis,
    Reports,
    Settings,
    Diagram
}

/// <summary>
/// The state shared by all screens of one working session: the connection, the extracted schema and the current page.
/// View models talk to each other only through this object.
/// </summary>
public partial class WorkspaceSession : ObservableObject
{
    [NotifyPropertyChangedFor(nameof(HasConnection))]
    [ObservableProperty] private ConnectionSettings? connection;

    [NotifyPropertyChangedFor(nameof(HasSchema))]
    [ObservableProperty] private DatabaseSchema? schema;

    [ObservableProperty] private string schemaJson = "";
    [ObservableProperty] private SchemaInsights? insights;
    [ObservableProperty] private AppSection currentSection = AppSection.Connect;

    /// <summary>Number of saved reports, shown as a badge in the sidebar.</summary>
    [ObservableProperty] private int reportCount;

    public IReadOnlyList<string> Databases { get; private set; } = Array.Empty<string>();

    public bool HasConnection => Connection is not null;
    public bool HasSchema => Schema is not null;

    /// <summary>Raised when a different database was connected, so dependent screens reset themselves.</summary>
    public event Action? ConnectionChanged;

    /// <summary>Raised when a new schema has been extracted.</summary>
    public event Action? SchemaChanged;

    /// <summary>Raised with the report id after a report has been written to the history.</summary>
    public event Action<string>? ReportSaved;

    /// <summary>Raised when a screen asks to open a specific report in the viewer.</summary>
    public event Action<string>? ReportOpenRequested;

    public void NotifyReportSaved(string id) => ReportSaved?.Invoke(id);

    public void OpenReport(string id)
    {
        CurrentSection = AppSection.Reports;
        ReportOpenRequested?.Invoke(id);
    }

    public void SetConnection(ConnectionSettings settings, IReadOnlyList<string> databases)
    {
        Databases = databases;
        Connection = settings;
        Schema = null;
        SchemaJson = "";
        Insights = null;
        ConnectionChanged?.Invoke();
    }

    public void SetSchema(DatabaseSchema schema, string json, SchemaInsights insights)
    {
        Schema = schema;
        SchemaJson = json;
        Insights = insights;
        SchemaChanged?.Invoke();
    }

    public void Reset()
    {
        Databases = Array.Empty<string>();
        Connection = null;
        Schema = null;
        SchemaJson = "";
        Insights = null;
        ConnectionChanged?.Invoke();
        CurrentSection = AppSection.Connect;
    }

    /// <summary>Navigates to a screen if its prerequisites are met.</summary>
    public bool TryNavigate(AppSection section)
    {
        bool allowed = section switch
        {
            AppSection.Scope => HasConnection,
            AppSection.Insights or AppSection.Analysis or AppSection.Diagram => HasSchema,
            _ => true
        };

        if (allowed) CurrentSection = section;
        return allowed;
    }
}
