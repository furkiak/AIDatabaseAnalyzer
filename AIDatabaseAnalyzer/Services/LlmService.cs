using System.Text;
using System.Text.Json;

namespace AIDatabaseAnalyzer.Services;

public class LlmService : ILlmService
{
    private readonly HttpClient _httpClient;

    public LlmService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
    }

    public async Task<string> AskGeminiAsync(string prompt)
    {
        string apiKey = await SecureStorage.Default.GetAsync("GeminiApiKey");
        string modelName = Preferences.Get("GeminiModel", "gemini-3.1-flash-lite-preview");

        if (string.IsNullOrWhiteSpace(apiKey)) throw new Exception("Gemini API Key is missing. Please check settings.");

        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={apiKey}";
        var requestBody = new { contents = new[] { new { parts = new[] { new { text = prompt } } } } };

        var response = await _httpClient.PostAsync(url, new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"));
        if (!response.IsSuccessStatusCode) throw new Exception($"Gemini Error: {response.StatusCode}");

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);
        return doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
    }

    public async Task<string> AskOllamaAsync(string prompt)
    {
        string ollamaUrl = Preferences.Get("OllamaUrl", "http://localhost:11434").TrimEnd('/');
        string modelName = Preferences.Get("OllamaModel", "qwen2.5-coder");

        var requestBody = new
        {
            model = modelName,
            prompt = prompt,
            stream = false,
            options = new { num_ctx = 128000 }
        };

        var response = await _httpClient.PostAsync($"{ollamaUrl}/api/generate",
            new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"));

        if (!response.IsSuccessStatusCode) throw new Exception("Ollama is not responding. Is it running?");

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);
        return doc.RootElement.GetProperty("response").GetString();
    }
}