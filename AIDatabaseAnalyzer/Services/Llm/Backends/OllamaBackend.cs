using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIDatabaseAnalyzer.Services.Llm.Backends;

/// <summary>Local models through Ollama's native chat API.</summary>
internal sealed class OllamaBackend : ILlmBackend
{
    private const double Temperature = 0.3;
    private readonly HttpTransport _transport;

    public OllamaBackend(HttpTransport transport) => _transport = transport;

    public async Task<LlmResult> CompleteAsync(LlmSettings settings, LlmRequest request, CancellationToken ct)
    {
        string model = settings.ModelName;
        if (model.Length == 0) throw new LlmException("No Ollama model is selected. Choose one in Settings.");
        EnsureFits(settings, request);

        var messages = new JsonArray();
        if (request.System.Length > 0) messages.Add(new JsonObject { ["role"] = "system", ["content"] = request.System });
        foreach (var m in request.Messages)
            messages.Add(new JsonObject { ["role"] = m.Role == LlmRole.User ? "user" : "assistant", ["content"] = m.Content });

        var options = new JsonObject { ["num_ctx"] = settings.ContextTokens, ["temperature"] = Temperature };
        if (request.MaxOutputTokens is { } max) options["num_predict"] = max;

        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = messages,
            ["stream"] = false,
            ["keep_alive"] = "15m",
            ["options"] = options
        };
        string payload = body.ToJsonString();
        string url = HttpTransport.NormalizeBaseUrl(settings) + "/api/chat";

        string response = await _transport.SendAsync(
            () => HttpTransport.Json(HttpMethod.Post, url, payload),
            "Ollama request", ct, Unreachable(settings), ParseError).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(response);
        var root = doc.RootElement;
        if (root.TryGetProperty("error", out var error))
            throw new LlmException("Ollama: " + error.GetString());

        string text = root.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content)
            ? content.GetString() ?? ""
            : "";

        long input = root.TryGetProperty("prompt_eval_count", out var p) && p.TryGetInt64(out long pi) ? pi : 0;
        long output = root.TryGetProperty("eval_count", out var e) && e.TryGetInt64(out long ei) ? ei : 0;
        bool truncated = root.TryGetProperty("done_reason", out var done) && done.GetString() == "length";

        return new LlmResult { Text = text, Usage = new LlmUsage(input, output, 0), Truncated = truncated, Model = model };
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(LlmSettings settings, CancellationToken ct)
    {
        string url = HttpTransport.NormalizeBaseUrl(settings) + "/api/tags";
        string body = await _transport.SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, url),
            "Listing Ollama models", ct, Unreachable(settings), ParseError).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(body);
        var names = new List<string>();
        if (doc.RootElement.TryGetProperty("models", out var models))
            foreach (var m in models.EnumerateArray())
                if (m.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } name) names.Add(name);
        return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Ollama silently truncates prompts that do not fit; refuse early with an actionable message instead.</summary>
    internal static void EnsureFits(LlmSettings settings, LlmRequest request)
    {
        int estimated = request.EstimatedTokens;
        if (estimated + 1024 > settings.ContextTokens)
            throw new LlmException(
                $"The prompt (≈{estimated:N0} tokens) does not fit into the configured context window ({settings.ContextTokens:N0} tokens). " +
                "Select fewer tables, disable sample data or SQL definitions, or increase the context size in Settings.");
    }

    private static string Unreachable(LlmSettings settings) =>
        $"Ollama is not reachable at {settings.EndpointUrl}. Is it running? (start it with “ollama serve”)";

    private static string? ParseError(string body)
    {
        string? message = HttpTransport.ParseError(body);
        return message is not null && message.Contains("not found", StringComparison.OrdinalIgnoreCase)
            ? message + " — install it with “ollama pull <model>”."
            : message;
    }
}
