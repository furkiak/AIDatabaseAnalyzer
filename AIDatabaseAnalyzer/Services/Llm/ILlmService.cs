namespace AIDatabaseAnalyzer.Services.Llm;

public interface ILlmService
{
    /// <summary>Sends one request to the provider in <paramref name="settings"/> and returns the cleaned-up answer.</summary>
    Task<LlmResult> CompleteAsync(LlmSettings settings, LlmRequest request, CancellationToken ct = default);

    /// <summary>Checks that the server / API key works and that the configured model exists.</summary>
    Task<LlmTestResult> TestAsync(LlmSettings settings, CancellationToken ct = default);

    /// <summary>Lists the models the provider offers that can generate text.</summary>
    Task<IReadOnlyList<string>> ListModelsAsync(LlmSettings settings, CancellationToken ct = default);
}

/// <summary>One provider's wire protocol.</summary>
internal interface ILlmBackend
{
    Task<LlmResult> CompleteAsync(LlmSettings settings, LlmRequest request, CancellationToken ct);
    Task<IReadOnlyList<string>> ListModelsAsync(LlmSettings settings, CancellationToken ct);
}
