using AIDatabaseAnalyzer.Services.Llm;

namespace AIDatabaseAnalyzer.Services.Infrastructure;

/// <inheritdoc cref="ISettingsService"/>
public sealed class SettingsService : ISettingsService
{
    private const string ApiKeyName = "GeminiApiKey";

    private readonly ISecretStore _secrets;
    private string _geminiApiKey = "";
    private bool _initialized;

    public SettingsService(ISecretStore secrets) => _secrets = secrets;

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _geminiApiKey = await _secrets.GetAsync(ApiKeyName).ConfigureAwait(false) ?? "";
        _initialized = true;
    }

    public LlmProvider Provider
    {
        get => Preferences.Default.Get("SelectedEngine", "Ollama") == "Gemini" ? LlmProvider.Gemini : LlmProvider.Ollama;
        set => Preferences.Default.Set("SelectedEngine", value == LlmProvider.Gemini ? "Gemini" : "Ollama");
    }

    public string OllamaUrl
    {
        get => Preferences.Default.Get("OllamaUrl", "http://localhost:11434");
        set => Preferences.Default.Set("OllamaUrl", (value ?? "").Trim());
    }

    public string OllamaModel
    {
        get => Preferences.Default.Get("OllamaModel", "qwen2.5-coder");
        set => Preferences.Default.Set("OllamaModel", (value ?? "").Trim());
    }

    public int OllamaContext
    {
        get => Math.Clamp(Preferences.Default.Get("OllamaContext", 32768), 2048, 1_000_000);
        set => Preferences.Default.Set("OllamaContext", Math.Clamp(value, 2048, 1_000_000));
    }

    public string GeminiModel
    {
        get => Preferences.Default.Get("GeminiModel", "gemini-3.1-flash-lite-preview");
        set => Preferences.Default.Set("GeminiModel", (value ?? "").Trim());
    }

    public string GeminiApiKey => _geminiApiKey;

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

    public async Task SetGeminiApiKeyAsync(string? apiKey)
    {
        string key = (apiKey ?? "").Trim();
        _geminiApiKey = key;
        if (key.Length == 0) await _secrets.RemoveAsync(ApiKeyName).ConfigureAwait(false);
        else await _secrets.SetAsync(ApiKeyName, key).ConfigureAwait(false);
    }

    public LlmSettings GetLlmSettings() => new()
    {
        Provider = Provider,
        OllamaUrl = OllamaUrl,
        OllamaModel = OllamaModel,
        OllamaContext = OllamaContext,
        GeminiModel = GeminiModel,
        GeminiApiKey = _geminiApiKey
    };
}
