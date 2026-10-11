using AIDatabaseAnalyzer.Services.Llm;

namespace AIDatabaseAnalyzer.Services.Infrastructure;

public enum ThemeMode
{
    System = 0,
    Light = 1,
    Dark = 2
}

/// <summary>User preferences. Secrets (API key, passwords) are kept in the platform's secure storage.</summary>
public interface ISettingsService : ILlmSettingsProvider
{
    /// <summary>Loads secrets from secure storage. Call once before reading <see cref="GeminiApiKey"/>.</summary>
    Task InitializeAsync();

    LlmProvider Provider { get; set; }
    string OllamaUrl { get; set; }
    string OllamaModel { get; set; }
    int OllamaContext { get; set; }
    string GeminiModel { get; set; }
    string GeminiApiKey { get; }

    string ReportLanguage { get; set; }
    ThemeMode Theme { get; set; }
    bool MaskSampleData { get; set; }

    /// <summary>False when the platform could not store secrets; they then only live until the app is closed.</summary>
    bool SecretsArePersistent { get; }

    Task SetGeminiApiKeyAsync(string? apiKey);
}

/// <summary>Stores small secrets. Falls back to memory when secure storage is unavailable (e.g. unsigned macOS builds).</summary>
public interface ISecretStore
{
    bool IsPersistent { get; }
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value);
    Task RemoveAsync(string key);
}
