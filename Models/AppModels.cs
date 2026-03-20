using CommunityToolkit.Mvvm.ComponentModel;

namespace AIDatabaseAnalyzer.Models;

public enum AnalysisStatus { Pending, Processing, Completed, Error }

public partial class AnalysisTopic : ObservableObject
{
    [ObservableProperty] private string title;
    [ObservableProperty] private string prompt;
    [ObservableProperty] private bool isSelected = true;
    [ObservableProperty] private bool isCustom = false;
    [ObservableProperty] private AnalysisStatus status = AnalysisStatus.Pending;
}

public partial class ComponentSelection : ObservableObject
{
    [ObservableProperty] private string name;
    [ObservableProperty] private bool isSelected = true;
}

public partial class TableSelection : ObservableObject
{
    [ObservableProperty] private string name;
    [ObservableProperty] private bool isSelected = true;
}