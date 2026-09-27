namespace AIDatabaseAnalyzer.Services;

public sealed class MauiSettingsStore : IAppSettingsStore
{
    public string Get(string key, string defaultValue) => Preferences.Get(key, defaultValue);
    public void Set(string key, string value) => Preferences.Set(key, value);
    public Task<string?> GetSecretAsync(string key) => SecureStorage.Default.GetAsync(key);
    public Task SetSecretAsync(string key, string value) => SecureStorage.Default.SetAsync(key, value);
    public void RemoveSecret(string key) => SecureStorage.Default.Remove(key);
}
