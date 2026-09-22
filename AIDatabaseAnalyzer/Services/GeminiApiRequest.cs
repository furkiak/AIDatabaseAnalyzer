using System.Text;
using System.Text.Json;

namespace AIDatabaseAnalyzer.Services;

public static class GeminiApiRequest
{
    public static HttpRequestMessage Create(string model, string apiKey, string prompt)
    {
        if (string.IsNullOrWhiteSpace(model)) throw new ArgumentException("Choose a Gemini model.", nameof(model));
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("Enter a Gemini API key.", nameof(apiKey));

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent";
        var body = new { contents = new[] { new { parts = new[] { new { text = prompt } } } } };
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("x-goog-api-key", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return request;
    }
}
