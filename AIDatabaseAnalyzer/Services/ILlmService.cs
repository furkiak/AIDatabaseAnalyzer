namespace AIDatabaseAnalyzer.Services;

public interface ILlmService
{
    Task<string> AskOllamaAsync(string prompt, CancellationToken cancellationToken = default);
    Task<string> AskGeminiAsync(string prompt, CancellationToken cancellationToken = default);
}
