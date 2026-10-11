namespace AIDatabaseAnalyzer.Services.Infrastructure;

/// <inheritdoc cref="ISecretStore"/>
public sealed class SecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _memory = new();
    private readonly object _gate = new();
    private bool _secureStorageBroken;

    public bool IsPersistent => !_secureStorageBroken;

    public async Task<string?> GetAsync(string key)
    {
        lock (_gate)
        {
            if (_memory.TryGetValue(key, out var cached)) return cached;
        }

        if (_secureStorageBroken) return null;
        try
        {
            return await SecureStorage.Default.GetAsync(key).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Keychain / DPAPI unavailable or the stored value cannot be decrypted any more.
            _secureStorageBroken = true;
            return null;
        }
    }

    public async Task SetAsync(string key, string value)
    {
        lock (_gate) _memory[key] = value;
        if (_secureStorageBroken) return;
        try
        {
            await SecureStorage.Default.SetAsync(key, value).ConfigureAwait(false);
        }
        catch (Exception)
        {
            _secureStorageBroken = true;
        }
    }

    public Task RemoveAsync(string key)
    {
        lock (_gate) _memory.Remove(key);
        if (!_secureStorageBroken)
        {
            try
            {
                SecureStorage.Default.Remove(key);
            }
            catch (Exception)
            {
                _secureStorageBroken = true;
            }
        }
        return Task.CompletedTask;
    }
}
