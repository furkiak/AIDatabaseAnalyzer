using System.Text.Json;

namespace AIDatabaseAnalyzer.Services;

public sealed record AppearancePreferences(string Theme = "Light", int TextScalePercent = 100)
{
    public void Validate()
    {
        if (Theme is not ("Light" or "Dark") || TextScalePercent is not (100 or 150 or 200))
            throw new InvalidDataException("Appearance supports Light/Dark and 100/150/200 percent text only.");
    }

    public double FontSize(double baseSize) { Validate(); return baseSize * TextScalePercent / 100d; }
}

public sealed class AppearancePreferencesStore(AppProfile profile)
{
    public string FilePath => profile.FilePath("appearance.json");

    public AppearancePreferences Load()
    {
        string path = FilePath;
        if (!File.Exists(path)) return new();
        using var document = AtomicProfileFile.Read(path);
        var root = document.RootElement;
        if (root.GetProperty("FormatVersion").GetInt32() != 1)
            throw new InvalidDataException("Unsupported appearance file; it was preserved.");
        var value = new AppearancePreferences(
            root.TryGetProperty("Theme", out var theme) ? theme.GetString()! : "Light",
            root.TryGetProperty("TextScalePercent", out var scale) ? scale.GetInt32() : 100);
        value.Validate();
        return value;
    }

    public void Save(AppearancePreferences preferences)
    {
        preferences.Validate();
        _ = Load(); // Do not replace corrupt or forward-version data on an appearance save.
        AtomicProfileFile.Write(FilePath, JsonSerializer.Serialize(new { FormatVersion = 1, preferences.Theme, preferences.TextScalePercent }));
    }
}
