using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIDatabaseAnalyzer.Services;

public partial class AppearanceController : ObservableObject
{
    private readonly AppearancePreferencesStore _store;
    private AppearancePreferences _saved;
    public string ProfileLabel { get; }
    public string[] Themes { get; } = ["Light", "Dark"];
    public int[] TextSizes { get; } = [100, 150, 200];
    private string _selectedTheme;
    public string SelectedTheme { get => _selectedTheme; set => SetProperty(ref _selectedTheme, value); }
    private int _textScalePercent;
    public int TextScalePercent { get => _textScalePercent; set => SetProperty(ref _textScalePercent, value); }
    private string _status = "Appearance is local. Saving it does not save model settings.";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public AppearanceController(AppProfile profile)
    {
        _store = new(profile);
        _saved = _store.Load();
        _selectedTheme = _saved.Theme;
        _textScalePercent = _saved.TextScalePercent;
        ProfileLabel = profile.Label;
    }

    public void ApplySaved() => Apply(_saved);

    private static void Apply(AppearancePreferences preferences)
    {
        var app = Application.Current!;
        app.UserAppTheme = preferences.Theme == "Dark" ? AppTheme.Dark : AppTheme.Light;
        var colors = preferences.Theme == "Dark"
            ? new[] { "#0E1724", "#172438", "#21324B", "#F2F6FC", "#C3D0E1", "#91A9C6", "#ADCAFF", "#FFD0B5" }
            : new[] { "#EDF2F7", "#FFFFFF", "#F0F4F8", "#172A42", "#425570", "#61758E", "#2258C3", "#8B341B" };
        string[] keys = ["PageColor", "SurfaceColor", "InputColor", "TextColor", "MutedColor", "BoundaryColor", "AccentColor", "WarningColor"];
        for (int i = 0; i < keys.Length; i++) app.Resources[keys[i]] = Color.FromArgb(colors[i]);
        foreach (int size in new[] { 11, 12, 13, 14, 16, 18, 20, 24, 28, 30, 32 })
            app.Resources[$"Font{size}"] = preferences.FontSize(size);
    }

    [RelayCommand]
    private void SaveAppearance()
    {
        try
        {
            var preferences = new AppearancePreferences(SelectedTheme, TextScalePercent);
            _store.Save(preferences);
            _saved = preferences;
            ApplySaved();
            Status = $"Saved {preferences.Theme}, {preferences.TextScalePercent}% text. Model settings were not saved.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            SelectedTheme = _saved.Theme;
            TextScalePercent = _saved.TextScalePercent;
            Status = "Appearance could not be saved. Existing preferences were preserved; check the profile file and permissions.";
        }
    }
}
