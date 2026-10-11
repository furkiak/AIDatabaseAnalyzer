using System.Text.Json;
using System.Text.Json.Serialization;
using AIDatabaseAnalyzer.Models;

namespace AIDatabaseAnalyzer.Services.Infrastructure;

public interface IConnectionProfileStore
{
    IReadOnlyList<SavedConnection> Load();
    Task SaveAsync(SavedConnection profile, string? password);
    Task DeleteAsync(string id);
    Task<string?> GetPasswordAsync(string id);
}

[JsonSerializable(typeof(List<SavedConnection>))]
[JsonSourceGenerationOptions(WriteIndented = false)]
internal sealed partial class ProfileJsonContext : JsonSerializerContext
{
}

/// <summary>Saved connections: metadata in preferences, passwords in secure storage.</summary>
public sealed class ConnectionProfileStore : IConnectionProfileStore
{
    private const string PrefKey = "ConnectionProfiles";
    private readonly ISecretStore _secrets;

    public ConnectionProfileStore(ISecretStore secrets) => _secrets = secrets;

    public IReadOnlyList<SavedConnection> Load()
    {
        try
        {
            string json = Preferences.Default.Get(PrefKey, "");
            if (string.IsNullOrWhiteSpace(json)) return new List<SavedConnection>();
            return JsonSerializer.Deserialize(json, ProfileJsonContext.Default.ListSavedConnection) ?? new List<SavedConnection>();
        }
        catch (JsonException)
        {
            return new List<SavedConnection>();
        }
    }

    public async Task SaveAsync(SavedConnection profile, string? password)
    {
        var all = Load().ToList();
        all.RemoveAll(p => p.Id == profile.Id);

        profile.HasSavedPassword = !string.IsNullOrEmpty(password);
        all.Add(profile);
        Persist(all);

        if (!string.IsNullOrEmpty(password)) await _secrets.SetAsync(SecretKey(profile.Id), password).ConfigureAwait(false);
        else await _secrets.RemoveAsync(SecretKey(profile.Id)).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id)
    {
        var all = Load().Where(p => p.Id != id).ToList();
        Persist(all);
        await _secrets.RemoveAsync(SecretKey(id)).ConfigureAwait(false);
    }

    public Task<string?> GetPasswordAsync(string id) => _secrets.GetAsync(SecretKey(id));

    private static string SecretKey(string id) => "profile-pwd-" + id;

    private static void Persist(List<SavedConnection> profiles) =>
        Preferences.Default.Set(PrefKey, JsonSerializer.Serialize(profiles, ProfileJsonContext.Default.ListSavedConnection));
}
