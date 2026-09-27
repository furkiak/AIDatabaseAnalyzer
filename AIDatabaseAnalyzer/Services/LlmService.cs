using System.Text;
using System.Text.Json;

namespace AIDatabaseAnalyzer.Services;

public class LlmService : ILlmService
{
    private readonly HttpClient _httpClient;
    private readonly IAppSettingsStore _settings;

    public LlmService(HttpClient httpClient, IAppSettingsStore settings)
    {
        _httpClient = httpClient;
        _settings = settings;
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
    }

    public async Task<string> AskGeminiAsync(string prompt, CancellationToken cancellationToken = default)
    {
        string? apiKey = await _settings.GetSecretAsync("GeminiApiKey");
        string modelName = _settings.Get("GeminiModel", "gemini-3.1-flash-lite-preview");

        if (string.IsNullOrWhiteSpace(apiKey)) throw new Exception("Gemini API Key is missing. Please check settings.");

        using var request = GeminiApiRequest.Create(modelName, apiKey, prompt);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new Exception($"Gemini Error: {response.StatusCode}");

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(responseJson);
        return ReadAnswer(doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text"));
    }

    public async Task<string> AskOllamaAsync(string prompt, CancellationToken cancellationToken = default)
    {
        string ollamaUrl = _settings.Get("OllamaUrl", "http://localhost:11434").TrimEnd('/');
        string modelName = _settings.Get("OllamaModel", "qwen2.5-coder");

        var requestBody = new
        {
            model = modelName,
            prompt = prompt,
            stream = false,
            options = new { num_ctx = 16384 }
        };

        using var response = await _httpClient.PostAsync($"{ollamaUrl}/api/generate",
            new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"), cancellationToken);

        if (!response.IsSuccessStatusCode) throw new Exception("Ollama is not responding. Is it running?");

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(responseJson);
        return ReadAnswer(doc.RootElement.GetProperty("response"));
    }

    private static string ReadAnswer(JsonElement element)
    {
        string? answer = element.ValueKind == JsonValueKind.String ? element.GetString() : null;
        if (string.IsNullOrWhiteSpace(answer))
            throw new InvalidDataException("The model response did not contain a non-empty text answer.");
        return answer;
    }
}
