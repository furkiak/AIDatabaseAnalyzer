using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using AIDatabaseAnalyzer.Services;

namespace AIDatabaseAnalyzer.ConnectionTests;

public sealed class ProfilePreferencesTests : IDisposable
{
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "OwnedProfileFixtures", Guid.NewGuid().ToString("N"));
    private string Normal => Path.Combine(_root, "normal");
    private AppProfile NewProfile(string name = "isolated") => AppProfile.Resolve(["--data-directory", Path.Combine(_root, name)], Normal);

    [Fact]
    public void NoArgumentsChoosesNormalWithoutCreatingOrReadingIt()
    {
        var profile = AppProfile.Resolve([], Normal);
        Assert.False(profile.IsIsolated);
        Assert.Equal(Normal, profile.DirectoryPath);
        Assert.False(Directory.Exists(Normal));
    }

    [Theory]
    [InlineData("--data-directory")]
    [InlineData("--data-directory", "relative")]
    [InlineData("--data-directory=relative")]
    [InlineData("--wrong", "unused")]
    [InlineData("--data-directory", "", "--data-directory", "other")]
    public void MalformedArgumentsFailWithoutNormalFallback(params string[] arguments)
    {
        Assert.Throws<ArgumentException>(() => AppProfile.Resolve(arguments, Normal));
        Assert.False(Directory.Exists(Normal));
    }

    [Theory]
    [InlineData("same")]
    [InlineData("ancestor")]
    [InlineData("descendant")]
    [InlineData("drive-root")]
    public void NormalDirectoryOverlapIsRejectedBeforeCreatingAnything(string kind)
    {
        string target = kind switch { "same" => Normal, "ancestor" => _root, "drive-root" => Path.GetPathRoot(Normal)!, _ => Path.Combine(Normal, "nested") };
        Assert.Throws<ArgumentException>(() => AppProfile.Resolve(["--data-directory", target], Normal));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void ExistingUnownedOrUnsupportedMarkerFilesArePreserved()
    {
        string target = Path.Combine(_root, "unowned");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "user.txt"), "fixture-owned unrelated content");
        Assert.Throws<InvalidDataException>(() => AppProfile.Resolve(["--data-directory", target], Normal));
        Assert.Equal("fixture-owned unrelated content", File.ReadAllText(Path.Combine(target, "user.txt")));
        File.WriteAllText(Path.Combine(target, ".aidb-profile"), "future v9");
        Assert.Throws<InvalidDataException>(() => AppProfile.Resolve(["--data-directory", target], Normal));
        Assert.Equal("future v9", File.ReadAllText(Path.Combine(target, ".aidb-profile")));
    }

    [Fact]
    public void IsolatedFactoryAndAppearanceNeverAccessNormalSettings()
    {
        var profile = NewProfile();
        int normalCalls = 0;
        var store = AppSettingsStoreFactory.Create(profile, () => { normalCalls++; throw new InvalidOperationException("Normal settings must not be accessed."); });
        Assert.Equal("Ollama", store.Get("SelectedEngine", "Ollama"));
        store.Set("OllamaModel", "fixture-model");
        new AppearancePreferencesStore(profile).Save(new("Dark", 200));
        Assert.Equal(0, normalCalls);
        Assert.False(Directory.Exists(Normal));
        Assert.IsType<IsolatedSettingsStore>(store);
    }

    [Fact]
    public void SettingsRestartAndTwoExplicitProfilesRemainSeparate()
    {
        var one = NewProfile("one"); var two = NewProfile("two");
        new IsolatedSettingsStore(one).Set("OllamaModel", "only-one");
        Assert.Equal("only-one", new IsolatedSettingsStore(NewProfile("one")).Get("OllamaModel", "missing"));
        Assert.Equal("missing", new IsolatedSettingsStore(two).Get("OllamaModel", "missing"));
        Assert.False(Directory.Exists(Normal));
    }

    [Theory]
    [InlineData("{not-json")]
    [InlineData("{\"FormatVersion\":99,\"Values\":{}}")]
    [InlineData("{\"FormatVersion\":1,\"Values\":{\"x\":\"one\",\"x\":\"two\"}}")]
    public void InvalidSettingsCannotBeOverwrittenAndBackupIsPreserved(string content)
    {
        var profile = NewProfile();
        string path = profile.FilePath("settings.json");
        File.WriteAllText(path, content); File.WriteAllText(path + ".bak", "owned older backup");
        var store = new IsolatedSettingsStore(profile);
        Assert.ThrowsAny<Exception>(() => store.Get("SelectedEngine", "Ollama"));
        Assert.ThrowsAny<Exception>(() => store.Set("SelectedEngine", "Ollama"));
        Assert.Equal(content, File.ReadAllText(path));
        Assert.Equal("owned older backup", File.ReadAllText(path + ".bak"));
        // Appearance-only storage is independent even when model settings are unreadable.
        new AppearancePreferencesStore(profile).Save(new("Dark", 150));
        Assert.Equal(content, File.ReadAllText(path));
    }

    [Fact, Trait("Category", "WindowsProfile")]
    public async Task RealCurrentUserDpapiRoundtripDoesNotPersistPlaintextOrTouchOtherProfiles()
    {
        Assert.True(OperatingSystem.IsWindows());
        var profile = NewProfile(); var other = NewProfile("other");
        string secret = "owned-synthetic-secret-" + Guid.NewGuid().ToString("N");
        var store = new IsolatedSettingsStore(profile);
        await store.SetSecretAsync("GeminiApiKey", secret);
        string path = profile.FilePath("secrets.dpapi.json");
        byte[] original = File.ReadAllBytes(path);
        Assert.DoesNotContain(secret, Encoding.UTF8.GetString(original));
        Assert.Equal(secret, await new IsolatedSettingsStore(NewProfile()).GetSecretAsync("GeminiApiKey"));
        Assert.Null(await new IsolatedSettingsStore(other).GetSecretAsync("GeminiApiKey"));
        new AppearancePreferencesStore(profile).Save(new("Dark", 200));
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".bak"));
        store.RemoveSecret("GeminiApiKey");
        Assert.Null(await store.GetSecretAsync("GeminiApiKey"));
        Assert.False(Directory.Exists(Normal));
    }

    [Theory, Trait("Category", "WindowsProfile")]
    [InlineData("{not-json")]
    [InlineData("{\"FormatVersion\":99,\"Values\":{}}")]
    [InlineData("{\"FormatVersion\":1,\"Values\":{\"GeminiApiKey\":\"broken-cipher\"}}")]
    public async Task InvalidCredentialFilesHaveNoPlaintextFallbackAndCannotBeReplaced(string content)
    {
        Assert.True(OperatingSystem.IsWindows());
        var profile = NewProfile(); string path = profile.FilePath("secrets.dpapi.json");
        File.WriteAllText(path, content); File.WriteAllText(path + ".bak", "owned backup");
        var store = new IsolatedSettingsStore(profile);
        await Assert.ThrowsAnyAsync<Exception>(() => store.GetSecretAsync("GeminiApiKey"));
        await Assert.ThrowsAnyAsync<Exception>(() => store.SetSecretAsync("GeminiApiKey", "new-synthetic-value"));
        Assert.ThrowsAny<Exception>(() => store.RemoveSecret("GeminiApiKey"));
        Assert.Equal(content, File.ReadAllText(path)); Assert.Equal("owned backup", File.ReadAllText(path + ".bak"));
    }

    [Fact, Trait("Category", "WindowsProfile")]
    public void JunctionProfileAndLinkedPreferenceFilesAreRejectedWithoutWritingTarget()
    {
        Assert.True(OperatingSystem.IsWindows());
        var target = NewProfile("target");
        string link = Path.Combine(_root, "junction");
        var info = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("/c"); info.ArgumentList.Add("mklink"); info.ArgumentList.Add("/J"); info.ArgumentList.Add(link); info.ArgumentList.Add(target.DirectoryPath);
        using var process = System.Diagnostics.Process.Start(info)!;
        process.WaitForExit(); Assert.Equal(0, process.ExitCode);
        try
        {
            Assert.Throws<IOException>(() => AppProfile.Resolve(["--data-directory", link], Normal));
            Assert.Throws<IOException>(() => AppProfile.Resolve(["--data-directory", Path.Combine(link, "nested")], Normal));
            Assert.Single(Directory.GetFiles(target.DirectoryPath));
        }
        finally { Directory.Delete(link); } // Remove only the owned junction itself, never its target.
    }

    [Fact, Trait("Category", "WindowsProfile")]
    public void HardLinkedAppearanceFileIsRejectedAndItsOriginalBytesArePreserved()
    {
        Assert.True(OperatingSystem.IsWindows());
        var profile = NewProfile();
        string original = Path.Combine(_root, "owned-source.json");
        const string content = "{\"FormatVersion\":1,\"Theme\":\"Dark\",\"TextScalePercent\":150}";
        File.WriteAllText(original, content);
        string link = profile.FilePath("appearance.json");
        Assert.True(CreateHardLink(link, original, IntPtr.Zero));
        try
        {
            var store = new AppearancePreferencesStore(profile);
            Assert.Throws<IOException>(() => store.Load());
            Assert.Throws<IOException>(() => store.Save(new("Light", 200)));
            Assert.Equal(content, File.ReadAllText(original));
        }
        finally { File.Delete(link); } // Removes only the explicit fixture-owned link.
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr attributes);

    public void Dispose()
    {
        string ownedRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "OwnedProfileFixtures")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(_root).StartsWith(ownedRoot, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid cleanup target.");
        AppProfile.AssertNoLinks(_root);
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
