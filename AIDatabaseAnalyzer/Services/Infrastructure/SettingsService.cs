using AIDatabaseAnalyzer.Services.Llm;

namespace AIDatabaseAnalyzer.Services.Infrastructure;

/// <inheritdoc cref="ISettingsService"/>
public sealed class SettingsService : ISettingsService
{
    private readonly ISecretStore _secrets;
    private readonly Dictionary<LlmProvider, string> _apiKeys = new();
    private bool _initialized;

    public SettingsService(ISecretStore secrets) => _secrets = secrets;

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        foreach (var info in LlmProviders.All.Where(p => p.NeedsApiKey))
            _apiKeys[info.Provider] = await _secrets.GetAsync(SecretName(info.Provider)).ConfigureAwait(false) ?? "";
        _initialized = true;
    }

    // Preference names of the first two providers are kept from earlier versions so existing settings survive the update.
    private static string ModelKey(LlmProvider p) => p switch { LlmProvider.Ollama => "OllamaModel", LlmProvider.Gemini => "GeminiModel", _ => $"{p}Model" };
    private static string UrlKey(LlmProvider p) => p == LlmProvider.Ollama ? "OllamaUrl" : $"{p}Url";
    private static string ContextKey(LlmProvider p) => p == LlmProvider.Ollama ? "OllamaContext" : $"{p}Context";
    private static string SecretName(LlmProvider p) => p == LlmProvider.Gemini ? "GeminiApiKey" : $"{p}ApiKey";

    public LlmProvider Provider
    {
        get => Enum.TryParse(Preferences.Default.Get("SelectedEngine", "Ollama"), out LlmProvider p) && Enum.IsDefined(p) ? p : LlmProvider.Ollama;
        set => Preferences.Default.Set("SelectedEngine", value.ToString());
    }

    public string GetModel(LlmProvider provider) => Preferences.Default.Get(ModelKey(provider), LlmProviders.Get(provider).DefaultModel);
    public void SetModel(LlmProvider provider, string model) => Preferences.Default.Set(ModelKey(provider), (model ?? "").Trim());

    public string GetBaseUrl(LlmProvider provider)
    {
        var info = LlmProviders.Get(provider);
        // Only self-hosted providers show the address; cloud providers always use their public endpoint.
        return info.ShowsBaseUrl ? Preferences.Default.Get(UrlKey(provider), info.DefaultBaseUrl) : "";
    }

    public void SetBaseUrl(LlmProvider provider, string url) => Preferences.Default.Set(UrlKey(provider), (url ?? "").Trim());

    public int GetContextWindow(LlmProvider provider) => Math.Clamp(Preferences.Default.Get(ContextKey(provider), 32768), 2048, 2_000_000);
    public void SetContextWindow(LlmProvider provider, int tokens) => Preferences.Default.Set(ContextKey(provider), Math.Clamp(tokens, 2048, 2_000_000));

    public string GetApiKey(LlmProvider provider) => _apiKeys.TryGetValue(provider, out string? key) ? key : "";

    public async Task SetApiKeyAsync(LlmProvider provider, string? apiKey)
    {
        string key = (apiKey ?? "").Trim();
        _apiKeys[provider] = key;
        if (key.Length == 0) await _secrets.RemoveAsync(SecretName(provider)).ConfigureAwait(false);
        else await _secrets.SetAsync(SecretName(provider), key).ConfigureAwait(false);
    }

    public int MaxParallelRequests
    {
        get => Math.Clamp(Preferences.Default.Get("MaxParallelRequests", 3), 1, 8);
        set => Preferences.Default.Set("MaxParallelRequests", Math.Clamp(value, 1, 8));
    }

    public string ReportLanguage
    {
        get => Preferences.Default.Get("ReportLanguage", "English");
        set => Preferences.Default.Set("ReportLanguage", value);
    }

    public ThemeMode Theme
    {
        get => (ThemeMode)Math.Clamp(Preferences.Default.Get("Theme", 0), 0, 2);
        set => Preferences.Default.Set("Theme", (int)value);
    }

    public bool MaskSampleData
    {
        get => Preferences.Default.Get("MaskSampleData", true);
        set => Preferences.Default.Set("MaskSampleData", value);
    }

    public bool SecretsArePersistent => _secrets.IsPersistent;

    public LlmSettings GetLlmSettings() => GetLlmSettings(Provider);

    public LlmSettings GetLlmSettings(LlmProvider provider) => new()
    {
        Provider = provider,
        Model = GetModel(provider),
        ApiKey = GetApiKey(provider),
        BaseUrl = GetBaseUrl(provider),
        ContextWindow = GetContextWindow(provider),
        MaxParallelRequests = MaxParallelRequests
    };
}
