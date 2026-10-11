using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIDatabaseAnalyzer.Models;

public enum AnalysisStatus
{
    Pending,
    Processing,
    Completed,
    Error,
    Cancelled
}

/// <summary>Parts of the extracted schema a topic depends on.</summary>
[Flags]
public enum TopicNeeds
{
    None = 0,
    SampleData = 1,
    CodeDefinitions = 2,
    Triggers = 4,
    Views = 8,
    Routines = 16,
    Indexes = 32
}

/// <summary>An analysis question that can be sent to the AI model.</summary>
public partial class AnalysisTopic : ObservableObject
{
    [ObservableProperty] private string title = "";
    [ObservableProperty] private string prompt = "";
    [ObservableProperty] private string category = "";
    [ObservableProperty] private bool isSelected = true;
    [ObservableProperty] private bool isCustom;

    /// <summary>What the topic needs from the extraction (sample rows, code definitions…).</summary>
    public TopicNeeds Needs { get; init; }

    /// <summary>Why the current schema cannot fully answer this topic; null when nothing is missing.</summary>
    [NotifyPropertyChangedFor(nameof(HasLimitation))]
    [ObservableProperty] private string? limitation;

    public bool HasLimitation => !string.IsNullOrEmpty(Limitation);

    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(IsPending))]
    [NotifyPropertyChangedFor(nameof(IsProcessing))]
    [NotifyPropertyChangedFor(nameof(IsCompleted))]
    [NotifyPropertyChangedFor(nameof(IsFailed))]
    [ObservableProperty] private AnalysisStatus status = AnalysisStatus.Pending;

    [ObservableProperty] private string? errorMessage;

    public bool IsPending => Status == AnalysisStatus.Pending;
    public bool IsProcessing => Status == AnalysisStatus.Processing;
    public bool IsCompleted => Status == AnalysisStatus.Completed;
    public bool IsFailed => Status is AnalysisStatus.Error or AnalysisStatus.Cancelled;

    public string StatusText => Status switch
    {
        AnalysisStatus.Pending => "Waiting",
        AnalysisStatus.Processing => "Analyzing",
        AnalysisStatus.Completed => "Done",
        AnalysisStatus.Error => "Failed",
        AnalysisStatus.Cancelled => "Cancelled",
        _ => ""
    };

    /// <summary>Raised when the user removes a custom prompt.</summary>
    public event Action<AnalysisTopic>? RemoveRequested;

    [RelayCommand]
    private void Toggle() => IsSelected = !IsSelected;

    [RelayCommand]
    private void Remove() => RemoveRequested?.Invoke(this);
}

/// <summary>A selectable table row in the scope screen.</summary>
public partial class TableSelection : ObservableObject
{
    [ObservableProperty] private string name = "";
    [ObservableProperty] private bool isSelected = true;

    /// <summary>Estimated number of rows, shown as a hint ("12,400 rows").</summary>
    public string Detail { get; init; } = "";

    [RelayCommand]
    private void Toggle() => IsSelected = !IsSelected;
}
