using AIDatabaseAnalyzer.Services;

namespace AIDatabaseAnalyzer.ConnectionTests;

public sealed class AppearancePreferencesTests : IDisposable
{
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "OwnedAppearanceFixtures", Guid.NewGuid().ToString("N"));
    private AppProfile Profile => AppProfile.Resolve(["--data-directory", Path.Combine(_root, "isolated")], Path.Combine(_root, "normal"));

    [Fact]
    public void MissingAndLegacyFontDefaultsDoNotRewriteFiles()
    {
        var store = new AppearancePreferencesStore(Profile);
        Assert.Equal(new AppearancePreferences(), store.Load());
        Assert.False(File.Exists(store.FilePath));
        const string legacy = "{\"FormatVersion\":1,\"Theme\":\"Dark\"}";
        File.WriteAllText(store.FilePath, legacy);
        Assert.Equal(new AppearancePreferences("Dark", 100), store.Load());
        Assert.Equal(legacy, File.ReadAllText(store.FilePath));
    }

    [Theory]
    [InlineData("Light", 100, 14d)]
    [InlineData("Dark", 150, 21d)]
    [InlineData("Light", 200, 28d)]
    public void RestartLoadsRealFontScaleAndAtomicallyKeepsPriorPreference(string theme, int scale, double expected)
    {
        var store = new AppearancePreferencesStore(Profile);
        store.Save(new("Dark", 100)); byte[] previous = File.ReadAllBytes(store.FilePath);
        store.Save(new(theme, scale));
        Assert.Equal(previous, File.ReadAllBytes(store.FilePath + ".bak"));
        var preferences = new AppearancePreferencesStore(Profile).Load();
        Assert.Equal(new(theme, scale), preferences);
        Assert.Equal(expected, preferences.FontSize(14));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(store.FilePath)!, "*.tmp"));
    }

    [Theory]
    [InlineData("Dark", 125)]
    [InlineData("Dark", 0)]
    [InlineData("Unsupported", 100)]
    public void InvalidSavePreservesCurrentFileAndBackup(string theme, int scale)
    {
        var store = new AppearancePreferencesStore(Profile);
        store.Save(new()); store.Save(new("Dark", 150));
        byte[] current = File.ReadAllBytes(store.FilePath), backup = File.ReadAllBytes(store.FilePath + ".bak");
        Assert.Throws<InvalidDataException>(() => store.Save(new(theme, scale)));
        Assert.Equal(current, File.ReadAllBytes(store.FilePath)); Assert.Equal(backup, File.ReadAllBytes(store.FilePath + ".bak"));
    }

    [Theory]
    [InlineData("{not-json")]
    [InlineData("{\"FormatVersion\":9,\"Theme\":\"Dark\",\"TextScalePercent\":200}")]
    [InlineData("{\"FormatVersion\":1,\"Theme\":\"Dark\",\"TextScalePercent\":150.5}")]
    [InlineData("{\"FormatVersion\":1,\"Theme\":\"Dark\",\"TextScalePercent\":\"200\"}")]
    [InlineData("{\"FormatVersion\":1,\"Theme\":\"Dark\",\"TextScalePercent\":null}")]
    [InlineData("{\"FormatVersion\":1,\"Theme\":\"Dark\",\"TextScalePercent\":100,\"TextScalePercent\":200}")]
    public void CorruptOrForwardFormatNeverSilentlyFallsBackOrOverwrites(string content)
    {
        var store = new AppearancePreferencesStore(Profile);
        File.WriteAllText(store.FilePath, content); File.WriteAllText(store.FilePath + ".bak", "owned backup");
        Assert.ThrowsAny<Exception>(() => store.Load());
        Assert.ThrowsAny<Exception>(() => store.Save(new("Light", 100)));
        Assert.Equal(content, File.ReadAllText(store.FilePath)); Assert.Equal("owned backup", File.ReadAllText(store.FilePath + ".bak"));
    }

    [Fact]
    public void AppearanceSavePreservesModelRecipientKeyAndConsentPreferencesByteForByte()
    {
        var profile = Profile;
        var settings = new IsolatedSettingsStore(profile);
        settings.Set("SelectedEngine", "Gemini"); settings.Set("OllamaUrl", "http://127.0.0.1:11434");
        settings.Set("GeminiModel", "synthetic-fixture-model"); settings.Set("OwnedConsentSentinel", "unchanged");
        string path = profile.FilePath("settings.json");
        byte[] bytes = File.ReadAllBytes(path), backup = File.ReadAllBytes(path + ".bak");
        new AppearancePreferencesStore(profile).Save(new("Dark", 200));
        Assert.Equal(bytes, File.ReadAllBytes(path)); Assert.Equal(backup, File.ReadAllBytes(path + ".bak"));
        Assert.Equal("Gemini", settings.Get("SelectedEngine", "Ollama"));
        Assert.False(File.Exists(profile.FilePath("secrets.dpapi.json")));
    }

    [Fact]
    public void LockedDestinationPreservesExistingAppearanceAndLeavesNoTemporaryData()
    {
        var store = new AppearancePreferencesStore(Profile); store.Save(new("Dark", 150));
        byte[] bytes = File.ReadAllBytes(store.FilePath);
        using (var locked = new FileStream(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.ThrowsAny<IOException>(() => store.Save(new("Light", 200)));
        Assert.Equal(bytes, File.ReadAllBytes(store.FilePath));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(store.FilePath)!, "*.tmp"));
    }

    [Fact, Trait("Category", "WindowsProfile")]
    public void ReadOnlyDestinationRejectsAtomicReplacementWithoutLosingCurrentOrBackup()
    {
        Assert.True(OperatingSystem.IsWindows());
        var store = new AppearancePreferencesStore(Profile); store.Save(new()); store.Save(new("Dark", 150));
        byte[] bytes = File.ReadAllBytes(store.FilePath), backup = File.ReadAllBytes(store.FilePath + ".bak");
        File.SetAttributes(store.FilePath, File.GetAttributes(store.FilePath) | FileAttributes.ReadOnly);
        try
        {
            Assert.ThrowsAny<UnauthorizedAccessException>(() => store.Save(new("Light", 200)));
            Assert.Equal(bytes, File.ReadAllBytes(store.FilePath)); Assert.Equal(backup, File.ReadAllBytes(store.FilePath + ".bak"));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(store.FilePath)!, "*.tmp"));
        }
        finally { File.SetAttributes(store.FilePath, FileAttributes.Normal); }
    }

    public void Dispose()
    {
        string ownedRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "OwnedAppearanceFixtures")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(_root).StartsWith(ownedRoot, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid cleanup target.");
        AppProfile.AssertNoLinks(_root);
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
