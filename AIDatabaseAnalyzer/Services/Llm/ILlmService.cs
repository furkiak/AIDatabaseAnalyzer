namespace AIDatabaseAnalyzer.Services.Llm;

public interface ILlmService
{
    /// <summary>Sends a prompt to the model configured in the application settings.</summary>
    Task<string> GenerateAsync(string prompt, CancellationToken ct = default);

    Task<string> GenerateAsync(LlmSettings settings, string prompt, CancellationToken ct = default);

    /// <summary>Checks that the server / API key works and that the configured model exists.</summary>
    Task<LlmTestResult> TestAsync(LlmSettings settings, CancellationToken ct = default);

    /// <summary>Lists the models the provider offers (installed Ollama models, Gemini models that can generate text).</summary>
    Task<IReadOnlyList<string>> ListModelsAsync(LlmSettings settings, CancellationToken ct = default);
}
