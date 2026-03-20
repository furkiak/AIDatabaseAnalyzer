namespace AIDatabaseAnalyzer.Services;

public interface ILlmService
{
    Task<string> AskOllamaAsync(string prompt);
    Task<string> AskGeminiAsync(string prompt);
}