using CommunityToolkit.Mvvm.ComponentModel;

namespace AIDatabaseAnalyzer.Models;

public enum AnalysisStatus { Pending, Processing, Completed, Error }

public partial class AnalysisTopic : ObservableObject
{
    [ObservableProperty] public partial string Title { get; set; } = string.Empty;
    [ObservableProperty] public partial string Prompt { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsSelected { get; set; } = true;
    [ObservableProperty] public partial bool IsCustom { get; set; } = false;
    [ObservableProperty] public partial AnalysisStatus Status { get; set; } = AnalysisStatus.Pending;
}

public partial class ComponentSelection : ObservableObject
{
    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsSelected { get; set; } = true;
}

public partial class TableSelection : ObservableObject
{
    public Services.DatabaseObjectIdentity? Identity { get; init; }
    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsSelected { get; set; } = true;
}
