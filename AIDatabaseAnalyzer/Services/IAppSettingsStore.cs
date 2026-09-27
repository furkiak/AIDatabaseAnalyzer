namespace AIDatabaseAnalyzer.Services;

public interface IAppSettingsStore
{
    string Get(string key, string defaultValue);
    void Set(string key, string value);
    Task<string?> GetSecretAsync(string key);
    Task SetSecretAsync(string key, string value);
    void RemoveSecret(string key);
}

public static class AppSettingsStoreFactory
{
    public static IAppSettingsStore Create(AppProfile profile, Func<IAppSettingsStore> normalStore) =>
        profile.IsIsolated ? new IsolatedSettingsStore(profile) : normalStore();
}
