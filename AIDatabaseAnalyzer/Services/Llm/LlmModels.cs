namespace AIDatabaseAnalyzer.Services.Llm;

public enum LlmProvider
{
    Ollama = 0,
    Gemini = 1
}

/// <summary>A snapshot of everything needed to talk to the configured AI model.</summary>
public sealed class LlmSettings
{
    public LlmProvider Provider { get; set; } = LlmProvider.Ollama;

    public string OllamaUrl { get; set; } = "http://localhost:11434";
    public string OllamaModel { get; set; } = "qwen2.5-coder";

    /// <summary>Context window requested from Ollama (tokens). Larger values need much more memory.</summary>
    public int OllamaContext { get; set; } = 32768;

    public string GeminiModel { get; set; } = "gemini-3.1-flash-lite-preview";
    public string GeminiApiKey { get; set; } = "";

    public string ProviderName => Provider == LlmProvider.Gemini ? "Gemini" : "Ollama";
    public string ModelName => Provider == LlmProvider.Gemini ? GeminiModel : OllamaModel;
    public bool IsLocal => Provider == LlmProvider.Ollama;

    /// <summary>Approximate number of prompt tokens the selected model can take.</summary>
    public int ContextTokens => Provider == LlmProvider.Gemini ? 1_000_000 : Math.Max(OllamaContext, 2048);

    public LlmSettings Clone() => (LlmSettings)MemberwiseClone();
}

/// <summary>Supplies the current settings; implemented by the application's settings store.</summary>
public interface ILlmSettingsProvider
{
    LlmSettings GetLlmSettings();
}

public sealed class LlmTestResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public IReadOnlyList<string> Models { get; init; } = Array.Empty<string>();
}

/// <summary>An AI request failed. The message is safe to show to the user.</summary>
public sealed class LlmException : Exception
{
    public LlmException(string message, bool isTransient = false, Exception? inner = null) : base(message, inner) =>
        IsTransient = isTransient;

    public bool IsTransient { get; }
}
