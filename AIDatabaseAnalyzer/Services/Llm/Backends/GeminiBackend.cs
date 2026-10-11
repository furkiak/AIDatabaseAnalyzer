using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIDatabaseAnalyzer.Services.Llm.Backends;

/// <summary>Google Gemini through the Generative Language REST API.</summary>
internal sealed class GeminiBackend : ILlmBackend
{
    private const double Temperature = 0.3;
    private const string Unreachable = "Cannot reach the Gemini API. Check your internet connection.";
    private readonly HttpTransport _transport;

    public GeminiBackend(HttpTransport transport) => _transport = transport;

    public async Task<LlmResult> CompleteAsync(LlmSettings settings, LlmRequest request, CancellationToken ct)
    {
        RequireKey(settings);
        string model = StripModelPrefix(settings.ModelName);
        if (model.Length == 0) throw new LlmException("No Gemini model is selected. Choose one in Settings.");

        var contents = new JsonArray();
        foreach (var m in request.Messages)
            contents.Add(new JsonObject
            {
                ["role"] = m.Role == LlmRole.User ? "user" : "model",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = m.Content })
            });

        var generation = new JsonObject { ["temperature"] = Temperature };
        if (request.MaxOutputTokens is { } max) generation["maxOutputTokens"] = max;

        var body = new JsonObject { ["contents"] = contents, ["generationConfig"] = generation };
        if (request.System.Length > 0)
            body["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = request.System }) };
        string payload = body.ToJsonString();

        string url = $"{HttpTransport.NormalizeBaseUrl(settings)}/models/{Uri.EscapeDataString(model)}:generateContent";
        string response = await _transport.SendAsync(
            () => Request(HttpMethod.Post, url, settings.ApiKey, payload),
            "Gemini request", ct, Unreachable, HttpTransport.ParseError).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(response);
        var root = doc.RootElement;

        if (root.TryGetProperty("promptFeedback", out var feedback) && feedback.TryGetProperty("blockReason", out var reason))
            throw new LlmException($"Gemini blocked the request ({reason.GetString()}). Try a different model or reduce sensitive sample data.");

        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            throw new LlmException("Gemini returned no answer. Try again or choose another model.", isTransient: true);

        var candidate = candidates[0];
        var text = new StringBuilder();
        if (candidate.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts))
        {
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True) continue;
                if (part.TryGetProperty("text", out var t)) text.Append(t.GetString());
            }
        }

        string finish = candidate.TryGetProperty("finishReason", out var fr) ? fr.GetString() ?? "" : "";
        if (text.Length == 0)
            throw new LlmException($"Gemini returned an empty answer (finish reason: {(finish.Length > 0 ? finish : "unknown")}).", isTransient: finish != "SAFETY");

        LlmUsage usage = default;
        if (root.TryGetProperty("usageMetadata", out var meta))
            usage = new LlmUsage(Long(meta, "promptTokenCount"), Long(meta, "candidatesTokenCount") + Long(meta, "thoughtsTokenCount"), Long(meta, "cachedContentTokenCount"));

        string answeredBy = root.TryGetProperty("modelVersion", out var mv) ? mv.GetString() ?? model : model;
        return new LlmResult { Text = text.ToString(), Usage = usage, Truncated = finish == "MAX_TOKENS", Model = answeredBy };
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(LlmSettings settings, CancellationToken ct)
    {
        RequireKey(settings);
        string url = $"{HttpTransport.NormalizeBaseUrl(settings)}/models?pageSize=200";
        string body = await _transport.SendAsync(
            () => Request(HttpMethod.Get, url, settings.ApiKey, null),
            "Listing Gemini models", ct, Unreachable, HttpTransport.ParseError).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(body);
        var names = new List<string>();
        if (doc.RootElement.TryGetProperty("models", out var models))
        {
            foreach (var m in models.EnumerateArray())
            {
                if (!SupportsGenerateContent(m)) continue;
                string name = StripModelPrefix(m.GetProperty("name").GetString() ?? "");
                if (IsTextModel(name)) names.Add(name);
            }
        }
        return names.Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static HttpRequestMessage Request(HttpMethod method, string url, string apiKey, string? json) =>
        // The key travels in a header, never in the URL, so it cannot end up in logs or proxies.
        HttpTransport.Json(method, url, json, h => h.Add("x-goog-api-key", apiKey));

    private static void RequireKey(LlmSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new LlmException("The Gemini API key is missing. Add it in Settings.");
    }

    private static long Long(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.TryGetInt64(out long l) ? l : 0;

    private static bool SupportsGenerateContent(JsonElement model) =>
        model.TryGetProperty("supportedGenerationMethods", out var methods)
        && methods.EnumerateArray().Any(m => m.GetString() == "generateContent");

    private static bool IsTextModel(string name)
    {
        string n = name.ToLowerInvariant();
        string[] excluded = { "embedding", "imagen", "veo", "tts", "aqa", "image", "audio", "live", "robotics", "computer-use" };
        return n.StartsWith("gemini", StringComparison.Ordinal) && !excluded.Any(n.Contains);
    }

    internal static string StripModelPrefix(string model)
    {
        string m = model.Trim();
        return m.StartsWith("models/", StringComparison.OrdinalIgnoreCase) ? m[7..] : m;
    }
}
