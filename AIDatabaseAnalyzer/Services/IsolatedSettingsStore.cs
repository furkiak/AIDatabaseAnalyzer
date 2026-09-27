using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AIDatabaseAnalyzer.Services;

public sealed class IsolatedSettingsStore : IAppSettingsStore
{
    private readonly AppProfile _profile;
    private readonly object _gate = new();
    public IsolatedSettingsStore(AppProfile profile)
    {
        if (!profile.IsIsolated) throw new ArgumentException("File settings require an explicit isolated profile.");
        _profile = profile;
    }

    private Dictionary<string, string> Load(string name)
    {
        string path = _profile.FilePath(name);
        if (!File.Exists(path)) return new(StringComparer.Ordinal);
        using var document = AtomicProfileFile.Read(path);
        if (document.RootElement.GetProperty("FormatVersion").GetInt32() != 1)
            throw new InvalidDataException("Unsupported profile settings; existing data was preserved.");
        var values = document.RootElement.GetProperty("Values");
        if (values.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid profile settings.");
        return values.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.GetString() ?? throw new InvalidDataException("Invalid settings value."), StringComparer.Ordinal);
    }

    private void Save(string name, Dictionary<string, string> values) => AtomicProfileFile.Write(_profile.FilePath(name),
        JsonSerializer.Serialize(new { FormatVersion = 1, Values = values }));

    private static void ValidateSecrets(Dictionary<string, string> values)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Isolated credentials require Windows current-user DPAPI.");
        foreach (string value in values.Values)
        {
            byte[] plain = ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public string Get(string key, string defaultValue)
    {
        lock (_gate) return Load("settings.json").GetValueOrDefault(key, defaultValue);
    }

    public void Set(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (_gate) { var values = Load("settings.json"); values[key] = value; Save("settings.json", values); }
    }

    public Task<string?> GetSecretAsync(string key)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Isolated credentials require Windows current-user DPAPI; there is no plaintext fallback.");
        lock (_gate)
        {
            var values = Load("secrets.dpapi.json");
            if (!values.TryGetValue(key, out string? encrypted)) return Task.FromResult<string?>(null);
            byte[] plain = ProtectedData.Unprotect(Convert.FromBase64String(encrypted), null, DataProtectionScope.CurrentUser);
            try { return Task.FromResult<string?>(Encoding.UTF8.GetString(plain)); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
    }

    public Task SetSecretAsync(string key, string value)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Isolated credentials require Windows current-user DPAPI; there is no plaintext fallback.");
        lock (_gate)
        {
            var values = Load("secrets.dpapi.json");
            ValidateSecrets(values);
            byte[] plain = Encoding.UTF8.GetBytes(value);
            try { values[key] = Convert.ToBase64String(ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser)); }
            finally { CryptographicOperations.ZeroMemory(plain); }
            Save("secrets.dpapi.json", values);
        }
        return Task.CompletedTask;
    }

    public void RemoveSecret(string key)
    {
        lock (_gate) { var values = Load("secrets.dpapi.json"); ValidateSecrets(values); if (values.Remove(key)) Save("secrets.dpapi.json", values); }
    }
}
