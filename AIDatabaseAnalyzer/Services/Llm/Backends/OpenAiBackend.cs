using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIDatabaseAnalyzer.Services.Llm.Backends;

/// <summary>
/// The OpenAI chat-completions protocol: OpenAI itself, DeepSeek and every compatible server
/// (LM Studio, vLLM, llama.cpp, OpenRouter, Groq, Mistral, …).
/// </summary>
internal sealed class OpenAiBackend : ILlmBackend
{
    private const double Temperature = 0.3;
    private readonly HttpTransport _transport;

    public OpenAiBackend(HttpTransport transport) => _transport = transport;

    public async Task<LlmResult> CompleteAsync(LlmSettings settings, LlmRequest request, CancellationToken ct)
    {
        RequireKey(settings);
        string model = settings.ModelName;
        if (model.Length == 0) throw new LlmException($"No {settings.ProviderName} model is selected. Choose one in Settings.");
        if (settings.Info.UserDefinedContext) OllamaBackend.EnsureFits(settings, request);

        var messages = new JsonArray();
        if (request.System.Length > 0) messages.Add(new JsonObject { ["role"] = "system", ["content"] = request.System });
        foreach (var m in request.Messages)
            messages.Add(new JsonObject { ["role"] = m.Role == LlmRole.User ? "user" : "assistant", ["content"] = m.Content });

        var body = new JsonObject { ["model"] = model, ["messages"] = messages, ["stream"] = false };
        switch (settings.Provider)
        {
            case LlmProvider.OpenAI:
                // Reasoning models only accept the default temperature, and "max_tokens" is rejected by them.
                if (request.MaxOutputTokens is { } openAiMax) body["max_completion_tokens"] = openAiMax;
                break;
            case LlmProvider.DeepSeek:
                bool reasoner = model.Contains("reasoner", StringComparison.OrdinalIgnoreCase);
                if (!reasoner) body["temperature"] = Temperature;
                // deepseek-chat answers with at most 4K tokens unless asked for more.
                body["max_tokens"] = request.MaxOutputTokens ?? (reasoner ? 32_768 : 8_192);
                break;
            default:
                body["temperature"] = Temperature;
                if (request.MaxOutputTokens is { } max) body["max_tokens"] = max;
                break;
        }

        string payload = body.ToJsonString();
        string url = HttpTransport.NormalizeBaseUrl(settings) + "/chat/completions";
        string response = await _transport.SendAsync(
            () => HttpTransport.Json(HttpMethod.Post, url, payload, h => Authorize(h, settings)),
            $"{settings.ProviderName} request", ct, Unreachable(settings), HttpTransport.ParseError).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(response);
        var root = doc.RootElement;
        if (root.TryGetProperty("error", out _))
            throw new LlmException($"{settings.ProviderName}: {HttpTransport.ParseError(response) ?? "unknown error"}");

        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            throw new LlmException($"{settings.ProviderName} returned no answer. Try again or choose another model.", isTransient: true);

        var choice = choices[0];
        string finish = choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String ? fr.GetString() ?? "" : "";
        string text = "";
        if (choice.TryGetProperty("message", out var message))
        {
            if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                text = content.GetString() ?? "";
            if (text.Length == 0 && message.TryGetProperty("refusal", out var refusal) && refusal.ValueKind == JsonValueKind.String)
                throw new LlmException($"{settings.ProviderName} declined the request: {refusal.GetString()}");
        }

        if (text.Length == 0 && finish == "content_filter")
            throw new LlmException($"{settings.ProviderName} filtered the answer. Try a different model or reduce sensitive sample data.");

        string answeredBy = root.TryGetProperty("model", out var mv) && mv.ValueKind == JsonValueKind.String ? mv.GetString() ?? model : model;
        return new LlmResult { Text = text, Usage = ReadUsage(root), Truncated = finish == "length", Model = answeredBy };
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(LlmSettings settings, CancellationToken ct)
    {
        RequireKey(settings);
        string url = HttpTransport.NormalizeBaseUrl(settings) + "/models";
        string body = await _transport.SendAsync(
            () => HttpTransport.Json(HttpMethod.Get, url, null, h => Authorize(h, settings)),
            $"Listing {settings.ProviderName} models", ct, Unreachable(settings), HttpTransport.ParseError).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        // OpenAI: {"data":[{"id":…}]}; a few servers return a bare array.
        var list = root.ValueKind == JsonValueKind.Array ? root : root.TryGetProperty("data", out var data) ? data : default;
        var names = new List<string>();
        if (list.ValueKind == JsonValueKind.Array)
            foreach (var m in list.EnumerateArray())
                if (m.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } name && IsChatModel(settings.Provider, name))
                    names.Add(name);

        return names.Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>OpenAI also lists embedding, audio, image and moderation models; keep only the ones that can chat.</summary>
    internal static bool IsChatModel(LlmProvider provider, string id)
    {
        if (provider != LlmProvider.OpenAI) return true;
        string n = id.ToLowerInvariant();
        bool family = n.StartsWith("gpt-", StringComparison.Ordinal) || n.StartsWith("chatgpt-", StringComparison.Ordinal)
                      || (n.Length > 1 && n[0] == 'o' && char.IsDigit(n[1]));
        string[] excluded = { "audio", "realtime", "tts", "transcribe", "search", "image", "embedding", "moderation", "instruct", "codex" };
        return family && !excluded.Any(n.Contains);
    }

    private static LlmUsage ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return default;
        long input = Long(usage, "prompt_tokens");
        long output = Long(usage, "completion_tokens");
        long cached = usage.TryGetProperty("prompt_tokens_details", out var details) && details.ValueKind == JsonValueKind.Object
            ? Long(details, "cached_tokens")
            : Long(usage, "prompt_cache_hit_tokens"); // DeepSeek
        return new LlmUsage(input, output, cached);
    }

    private static long Long(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.TryGetInt64(out long l) ? l : 0;

    private static void Authorize(System.Net.Http.Headers.HttpRequestHeaders headers, LlmSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.ApiKey.Trim());
    }

    private static void RequireKey(LlmSettings settings)
    {
        if (!settings.Info.ApiKeyOptional && string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new LlmException($"The {settings.ProviderName} API key is missing. Add it in Settings.");
    }

    private static string Unreachable(LlmSettings settings) => settings.Provider == LlmProvider.OpenAICompatible
        ? $"The server at {settings.EndpointUrl} is not reachable. Check the address (it usually ends with /v1) and that the server is running."
        : $"Cannot reach the {settings.ProviderName} API. Check your internet connection.";
}
