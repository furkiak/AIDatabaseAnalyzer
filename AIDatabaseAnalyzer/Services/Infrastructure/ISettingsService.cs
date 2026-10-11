using AIDatabaseAnalyzer.Services.Llm;

namespace AIDatabaseAnalyzer.Services.Infrastructure;

public enum ThemeMode
{
    System = 0,
    Light = 1,
    Dark = 2
}

/// <summary>User preferences. Secrets (API keys, passwords) are kept in the platform's secure storage.</summary>
public interface ISettingsService : ILlmSettingsProvider
{
    /// <summary>Loads secrets from secure storage. Call once before reading API keys.</summary>
    Task InitializeAsync();

    /// <summary>The provider used for analyses and chat.</summary>
    LlmProvider Provider { get; set; }

    /// <summary>Every provider keeps its own model, address, context size and key, so switching back and forth loses nothing.</summary>
    string GetModel(LlmProvider provider);
    void SetModel(LlmProvider provider, string model);
    string GetBaseUrl(LlmProvider provider);
    void SetBaseUrl(LlmProvider provider, string url);
    int GetContextWindow(LlmProvider provider);
    void SetContextWindow(LlmProvider provider, int tokens);
    string GetApiKey(LlmProvider provider);
    Task SetApiKeyAsync(LlmProvider provider, string? apiKey);

    /// <summary>Concurrent requests for cloud providers (1–8).</summary>
    int MaxParallelRequests { get; set; }

    /// <summary>Settings for a provider other than the selected one (used by the settings screen for drafts and tests).</summary>
    LlmSettings GetLlmSettings(LlmProvider provider);

    string ReportLanguage { get; set; }
    ThemeMode Theme { get; set; }
    bool MaskSampleData { get; set; }

    /// <summary>False when the platform could not store secrets; they then only live until the app is closed.</summary>
    bool SecretsArePersistent { get; }
}

/// <summary>Stores small secrets. Falls back to memory when secure storage is unavailable (e.g. unsigned macOS builds).</summary>
public interface ISecretStore
{
    bool IsPersistent { get; }
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value);
    Task RemoveAsync(string key);
}
