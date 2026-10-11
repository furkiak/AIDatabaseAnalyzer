using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AIDatabaseAnalyzer.Services.Infrastructure;
using AIDatabaseAnalyzer.Services.Insights;

namespace AIDatabaseAnalyzer.ViewModels;

/// <summary>A selectable filter pill ("All", "Critical", "Security"...).</summary>
public partial class FilterChip : ObservableObject
{
    private readonly Action<FilterChip> _select;

    public FilterChip(string key, string label, Action<FilterChip> select)
    {
        Key = key;
        Label = label;
        _select = select;
    }

    public string Key { get; }

    [NotifyPropertyChangedFor(nameof(Text))]
    [ObservableProperty] private string label;

    [NotifyPropertyChangedFor(nameof(Text))]
    [ObservableProperty] private int count = -1;

    [ObservableProperty] private bool isSelected;

    public string Text => Count >= 0 ? $"{Label}  {Count}" : Label;

    [RelayCommand]
    private void Select() => _select(this);
}

/// <summary>A selectable card with a title and a short caption (AI provider, database engine, topic preset).</summary>
public partial class ChoiceTile : ObservableObject
{
    private readonly Action<ChoiceTile> _select;

    public ChoiceTile(int key, string title, string caption, Action<ChoiceTile> select)
    {
        Key = key;
        Title = title;
        Caption = caption;
        _select = select;
    }

    public int Key { get; }
    public string Title { get; }
    public string Caption { get; }

    [ObservableProperty] private bool isSelected;

    [RelayCommand]
    private void Select() => _select(this);
}

/// <summary>A finding as displayed in the Insights list.</summary>
public partial class FindingItem : ObservableObject
{
    public FindingItem(Finding finding)
    {
        Finding = finding;
        SeverityColor = finding.Severity switch
        {
            FindingSeverity.Critical => Color.FromArgb("#B42318"),
            FindingSeverity.High => Color.FromArgb("#E5483E"),
            FindingSeverity.Medium => Color.FromArgb("#D98A00"),
            FindingSeverity.Low => Color.FromArgb("#2E7CF6"),
            _ => Color.FromArgb("#7A7A85")
        };
    }

    public Finding Finding { get; }
    public string Title => Finding.Title;
    public string Description => Finding.Description;
    public string Recommendation => Finding.Recommendation;
    public string SeverityText => Finding.Severity.ToString().ToUpperInvariant();
    public string Category => Finding.Category;
    public Color SeverityColor { get; }
    public string CountText => Finding.Count == 1 ? "1 object" : $"{Finding.Count} objects";
    public string ObjectsText => string.Join("\n", Finding.Objects);
    public bool HasObjects => Finding.Objects.Count > 0;

    [ObservableProperty] private bool isExpanded;

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;
}

/// <summary>A row of the report history.</summary>
public partial class ReportItem : ObservableObject
{
    private readonly Action<ReportItem> _open;
    private readonly Action<ReportItem> _delete;

    public ReportItem(ReportSummary summary, Action<ReportItem> open, Action<ReportItem> delete)
    {
        Summary = summary;
        _open = open;
        _delete = delete;
    }

    public ReportSummary Summary { get; }
    public string Id => Summary.Id;
    public string Title => Summary.DatabaseName;
    public string Subtitle => $"{Summary.EngineName} · {Summary.Provider} · {Summary.Model}";
    public string DateText => Summary.GeneratedAtUtc.ToLocalTime().ToString("g");
    public string SectionsText => Summary.FailedSections > 0
        ? $"{Summary.SectionCount} sections · {Summary.FailedSections} failed"
        : $"{Summary.SectionCount} sections";
    public string ScoreText => Summary.HealthScore > 0 ? $"{Summary.HealthScore}" : "–";
    public string GradeText => Summary.HealthScore > 0 ? $"Grade {Summary.HealthGrade}" : "";

    public Color ScoreColor => Summary.HealthScore switch
    {
        >= 80 => Color.FromArgb("#1F9D55"),
        >= 55 => Color.FromArgb("#D98A00"),
        > 0 => Color.FromArgb("#D92D20"),
        _ => Color.FromArgb("#8E8E93")
    };

    [RelayCommand]
    private void Open() => _open(this);

    [RelayCommand]
    private void Delete() => _delete(this);
}
