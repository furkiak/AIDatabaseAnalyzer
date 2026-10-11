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

/// <summary>An analysis question (system prompt) that can be sent to the AI model.</summary>
public partial class AnalysisTopic : ObservableObject
{
    [ObservableProperty] private string title = "";
    [ObservableProperty] private string prompt = "";
    [ObservableProperty] private string category = "";
    [ObservableProperty] private bool isSelected = true;
    [ObservableProperty] private bool isCustom;

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
