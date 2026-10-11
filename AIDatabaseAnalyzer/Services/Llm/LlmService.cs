using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIDatabaseAnalyzer.Services.Llm;

/// <summary>Talks to Ollama (local) and Google Gemini (cloud) with retries, back-off and readable errors.</summary>
public sealed class LlmService : ILlmService
{
    private const int MaxAttempts = 4;
    private const double Temperature = 0.3;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(10);

    private readonly HttpClient _http;
    private readonly ILlmSettingsProvider _settings;

    /// <summary>Base address of the Gemini REST API (overridable for tests and proxies).</summary>
    public string GeminiBase { get; set; } = "https://generativelanguage.googleapis.com/v1beta";

    public LlmService(HttpClient http, ILlmSettingsProvider settings)
    {
        _http = http;
        _settings = settings;
        // Timeouts are handled per request so that long local generations are not cut off by the client default.
        _http.Timeout = Timeout.InfiniteTimeSpan;
    }

    // ------------------------------------------------------------------ public API

    public Task<string> GenerateAsync(string prompt, CancellationToken ct = default) =>
        GenerateAsync(_settings.GetLlmSettings(), prompt, ct);

    public Task<string> GenerateAsync(LlmSettings settings, string prompt, CancellationToken ct = default) =>
        settings.Provider == LlmProvider.Gemini
            ? GenerateGeminiAsync(settings, prompt, ct)
            : GenerateOllamaAsync(settings, prompt, ct);

    public async Task<IReadOnlyList<string>> ListModelsAsync(LlmSettings settings, CancellationToken ct = default)
    {
        if (settings.Provider == LlmProvider.Gemini)
        {
            RequireKey(settings);
            string body = await SendAsync(
                () => GeminiRequest(HttpMethod.Get, $"{GeminiBase}/models?pageSize=200", settings.GeminiApiKey, null),
                "Listing Gemini models", ct, "Cannot reach the Gemini API. Check your internet connection.", ParseGeminiError).ConfigureAwait(false);

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
        else
        {
            string url = OllamaBase(settings) + "/api/tags";
            string body = await SendAsync(
                () => new HttpRequestMessage(HttpMethod.Get, url),
                "Listing Ollama models", ct, OllamaUnreachable(settings), ParseOllamaError).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(body);
            var names = new List<string>();
            if (doc.RootElement.TryGetProperty("models", out var models))
                foreach (var m in models.EnumerateArray())
                    if (m.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } name) names.Add(name);
            return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public async Task<LlmTestResult> TestAsync(LlmSettings settings, CancellationToken ct = default)
    {
        try
        {
            var models = await ListModelsAsync(settings, ct).ConfigureAwait(false);
            string wanted = settings.Provider == LlmProvider.Gemini ? StripModelPrefix(settings.GeminiModel) : settings.OllamaModel;
            bool found = models.Any(m => string.Equals(m, wanted, StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(m, wanted + ":latest", StringComparison.OrdinalIgnoreCase));

            string provider = settings.ProviderName;
            if (found)
                return new LlmTestResult { Success = true, Models = models, Message = $"{provider} is ready — model “{wanted}” is available." };

            string hint = settings.Provider == LlmProvider.Ollama
                ? $"Install it with:  ollama pull {wanted}"
                : "Pick one of the available models in the list.";
            return new LlmTestResult
            {
                Success = false,
                Models = models,
                Message = $"Connected to {provider}, but model “{wanted}” was not found ({models.Count} models available). {hint}"
            };
        }
        catch (LlmException ex)
        {
            return new LlmTestResult { Success = false, Message = ex.Message };
        }
    }

    // ------------------------------------------------------------------ Gemini

    private async Task<string> GenerateGeminiAsync(LlmSettings settings, string prompt, CancellationToken ct)
    {
        RequireKey(settings);
        string model = StripModelPrefix(settings.GeminiModel);
        if (string.IsNullOrWhiteSpace(model))
            throw new LlmException("No Gemini model is selected. Choose one in Settings.");

        var body = new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = prompt })
            }),
            ["generationConfig"] = new JsonObject { ["temperature"] = Temperature }
        };
        string payload = body.ToJsonString();

        string response = await SendAsync(
            () => GeminiRequest(HttpMethod.Post, $"{GeminiBase}/models/{Uri.EscapeDataString(model)}:generateContent", settings.GeminiApiKey, payload),
            "Gemini request", ct, "Cannot reach the Gemini API. Check your internet connection.", ParseGeminiError).ConfigureAwait(false);

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

        if (text.Length == 0)
        {
            string finish = candidate.TryGetProperty("finishReason", out var fr) ? fr.GetString() ?? "unknown" : "unknown";
            throw new LlmException($"Gemini returned an empty answer (finish reason: {finish}).", isTransient: finish != "SAFETY");
        }

        if (candidate.TryGetProperty("finishReason", out var finishReason) && finishReason.GetString() == "MAX_TOKENS")
            text.Append("\n\n*The model stopped because it reached its output limit; the answer may be incomplete.*");

        return text.ToString();
    }

    private static HttpRequestMessage GeminiRequest(HttpMethod method, string url, string apiKey, string? json)
    {
        var request = new HttpRequestMessage(method, url);
        // The key travels in a header, never in the URL, so it cannot end up in logs or proxies.
        request.Headers.Add("x-goog-api-key", apiKey);
        if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    private static void RequireKey(LlmSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.GeminiApiKey))
            throw new LlmException("The Gemini API key is missing. Add it in Settings.");
    }

    private static bool SupportsGenerateContent(JsonElement model) =>
        model.TryGetProperty("supportedGenerationMethods", out var methods)
        && methods.EnumerateArray().Any(m => m.GetString() == "generateContent");

    private static bool IsTextModel(string name)
    {
        string n = name.ToLowerInvariant();
        string[] excluded = { "embedding", "imagen", "veo", "tts", "aqa", "image", "audio", "live", "robotics", "computer-use" };
        return n.StartsWith("gemini", StringComparison.Ordinal) && !excluded.Any(n.Contains);
    }

    private static string StripModelPrefix(string model)
    {
        string m = model.Trim();
        return m.StartsWith("models/", StringComparison.OrdinalIgnoreCase) ? m[7..] : m;
    }

    private static string? ParseGeminiError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                string? message = error.TryGetProperty("message", out var m) ? m.GetString() : null;
                string? status = error.TryGetProperty("status", out var s) ? s.GetString() : null;
                return status is null ? message : $"{status}: {message}";
            }
        }
        catch (JsonException)
        {
            // not JSON
        }
        return null;
    }

    // ------------------------------------------------------------------ Ollama

    private async Task<string> GenerateOllamaAsync(LlmSettings settings, string prompt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.OllamaModel))
            throw new LlmException("No Ollama model is selected. Choose one in Settings.");

        int estimatedTokens = (int)(prompt.Length / 3.5);
        if (estimatedTokens + 1024 > settings.ContextTokens)
            throw new LlmException(
                $"The prompt (≈{estimatedTokens:N0} tokens) does not fit into the configured context window ({settings.ContextTokens:N0} tokens). " +
                "Select fewer tables, disable sample data or SQL definitions, or increase the context size in Settings.");

        var body = new JsonObject
        {
            ["model"] = settings.OllamaModel,
            ["prompt"] = prompt,
            ["stream"] = false,
            ["keep_alive"] = "15m",
            ["options"] = new JsonObject { ["num_ctx"] = settings.ContextTokens, ["temperature"] = Temperature }
        };
        string payload = body.ToJsonString();
        string url = OllamaBase(settings) + "/api/generate";

        string response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(payload, Encoding.UTF8, "application/json") },
            "Ollama request", ct, OllamaUnreachable(settings), ParseOllamaError).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(response);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var error))
            throw new LlmException("Ollama: " + error.GetString());

        string text = root.TryGetProperty("response", out var r) ? r.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(text))
            throw new LlmException("Ollama returned an empty answer. Try again or use a larger model.", isTransient: true);

        if (root.TryGetProperty("done_reason", out var done) && done.GetString() == "length")
            text += "\n\n*The model stopped because it reached its output limit; the answer may be incomplete.*";
        return text;
    }

    private static string OllamaBase(LlmSettings settings)
    {
        string url = (settings.OllamaUrl ?? "").Trim().TrimEnd('/');
        if (url.Length == 0) url = "http://localhost:11434";
        if (!url.Contains("://", StringComparison.Ordinal)) url = "http://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            throw new LlmException($"“{settings.OllamaUrl}” is not a valid Ollama address (example: http://localhost:11434).");
        return url;
    }

    private static string OllamaUnreachable(LlmSettings settings) =>
        $"Ollama is not reachable at {settings.OllamaUrl}. Is it running? (start it with “ollama serve”)";

    private static string? ParseOllamaError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                string message = error.GetString() ?? "";
                return message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                    ? message + " — install it with “ollama pull <model>”."
                    : message;
            }
        }
        catch (JsonException)
        {
            // not JSON
        }
        return null;
    }

    // ------------------------------------------------------------------ transport

    private async Task<string> SendAsync(
        Func<HttpRequestMessage> create, string what, CancellationToken ct, string unreachableMessage, Func<string, string?> parseError)
    {
        for (int attempt = 1; ; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(RequestTimeout);

            HttpStatusCode status;
            string reason;
            string body;
            TimeSpan? retryAfter;

            try
            {
                using var request = create();
                using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
                body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                status = response.StatusCode;
                reason = response.ReasonPhrase ?? "";
                retryAfter = RetryAfter(response.Headers);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new LlmException($"{what} timed out after {RequestTimeout.TotalMinutes:0} minutes.", isTransient: true);
            }
            catch (HttpRequestException ex)
            {
                throw new LlmException(unreachableMessage, isTransient: false, ex);
            }

            if ((int)status is >= 200 and < 300) return body;

            bool transient = status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
                or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

            if (transient && attempt < MaxAttempts)
            {
                var delay = retryAfter ?? TimeSpan.FromSeconds(Math.Pow(2, attempt) * 1.5 + Random.Shared.NextDouble());
                if (delay > TimeSpan.FromSeconds(60)) delay = TimeSpan.FromSeconds(60);
                await Task.Delay(delay, ct).ConfigureAwait(false);
                continue;
            }

            string detail = parseError(body) ?? Shorten(body);
            string friendly = status switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "access was denied — check the API key",
                HttpStatusCode.NotFound => "not found — check the model name",
                HttpStatusCode.TooManyRequests => "rate limit or quota reached",
                _ => reason
            };
            throw new LlmException($"{what} failed ({(int)status} {friendly}). {detail}".Trim(), transient);
        }
    }

    private static TimeSpan? RetryAfter(HttpResponseHeaders headers)
    {
        var value = headers.RetryAfter;
        if (value is null) return null;
        if (value.Delta is { } delta) return delta;
        if (value.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }
        return null;
    }

    private static string Shorten(string text) => text.Length <= 300 ? text.Trim() : text[..300].Trim() + "…";
}
