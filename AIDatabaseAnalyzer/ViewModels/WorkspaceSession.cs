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
    Diagram,
    Chat,
    Snapshots
}

/// <summary>What the ER diagram should show when it opens: a table, or every table of one finding.</summary>
public sealed record DiagramFocus(string? Table, string? RuleId);

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

    /// <summary>The newest AI report of the current database (written or opened in this session); linked into the diagram.</summary>
    [ObservableProperty] private AnalysisReport? latestReport;

    /// <summary>Changes since the previous snapshot of this database, set when a schema is extracted.</summary>
    [ObservableProperty] private string? driftSummary;

    private DiagramFocus? _pendingFocus;
    private string? _pendingQuestion;

    public IReadOnlyList<string> Databases { get; private set; } = Array.Empty<string>();

    public bool HasConnection => Connection is not null;
    public bool HasSchema => Schema is not null;

    /// <summary>Raised when a different database was connected, so dependent screens reset themselves.</summary>
    public event Action? ConnectionChanged;

    /// <summary>Raised when a new schema has been extracted.</summary>
    public event Action? SchemaChanged;

    /// <summary>Raised with the report id after a report has been written to the history.</summary>
    public event Action<string>? ReportSaved;

    /// <summary>Raised when a screen asks to open a specific report in the viewer (optionally at a section index).</summary>
    public event Action<string, int?>? ReportOpenRequested;

    /// <summary>Raised when the diagram is already open and should switch to a new focus.</summary>
    public event Action? DiagramFocusRequested;

    /// <summary>Raised when another screen sends a question to the schema chat.</summary>
    public event Action? QuestionRequested;

    public void NotifyReportSaved(string id) => ReportSaved?.Invoke(id);

    /// <summary>Opens the ER diagram focused on a table or on the tables of a finding.</summary>
    public bool ShowInDiagram(string? table = null, string? ruleId = null)
    {
        if (!HasSchema) return false;
        _pendingFocus = new DiagramFocus(table, ruleId);
        if (CurrentSection == AppSection.Diagram)
        {
            DiagramFocusRequested?.Invoke();
            return true;
        }
        return TryNavigate(AppSection.Diagram);
    }

    /// <summary>Returns the pending diagram focus once.</summary>
    public DiagramFocus? TakeDiagramFocus()
    {
        var focus = _pendingFocus;
        _pendingFocus = null;
        return focus;
    }

    /// <summary>Opens the schema chat and asks a question there.</summary>
    public bool Ask(string question)
    {
        if (!HasSchema) return false;
        _pendingQuestion = question;
        if (CurrentSection == AppSection.Chat) QuestionRequested?.Invoke();
        else TryNavigate(AppSection.Chat);
        return true;
    }

    public string? TakeQuestion()
    {
        string? question = _pendingQuestion;
        _pendingQuestion = null;
        return question;
    }

    public void OpenReport(string id, int? section = null)
    {
        CurrentSection = AppSection.Reports;
        ReportOpenRequested?.Invoke(id, section);
    }

    public void SetConnection(ConnectionSettings settings, IReadOnlyList<string> databases)
    {
        Databases = databases;
        Connection = settings;
        Schema = null;
        SchemaJson = "";
        Insights = null;
        LatestReport = null;
        DriftSummary = null;
        ConnectionChanged?.Invoke();
    }

    public void SetSchema(DatabaseSchema schema, string json, SchemaInsights insights)
    {
        Schema = schema;
        SchemaJson = json;
        Insights = insights;
        if (LatestReport is { } report && report.DatabaseName != schema.DatabaseName) LatestReport = null;
        DriftSummary = null; // recomputed against the previous snapshot
        SchemaChanged?.Invoke();
    }

    public void Reset()
    {
        Databases = Array.Empty<string>();
        Connection = null;
        Schema = null;
        SchemaJson = "";
        Insights = null;
        LatestReport = null;
        DriftSummary = null;
        ConnectionChanged?.Invoke();
        CurrentSection = AppSection.Connect;
    }

    /// <summary>Navigates to a screen if its prerequisites are met.</summary>
    public bool TryNavigate(AppSection section)
    {
        bool allowed = section switch
        {
            AppSection.Scope => HasConnection,
            AppSection.Insights or AppSection.Analysis or AppSection.Diagram or AppSection.Chat => HasSchema,
            _ => true
        };

        if (allowed) CurrentSection = section;
        return allowed;
    }
}
