using System.Text;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using Anthropic.Models.Models;

namespace AIDatabaseAnalyzer.Services.Llm.Backends;

/// <summary>
/// Anthropic Claude through the official SDK. The system block (instructions + schema) carries a cache breakpoint,
/// so after the first topic the schema is read from the prompt cache instead of being processed again.
/// </summary>
internal sealed class AnthropicBackend : ILlmBackend
{
    /// <summary>Non-streaming requests stay well below HTTP time-outs with this output budget (thinking included).</summary>
    private const int DefaultMaxTokens = 16_000;

    /// <summary>Models that can hand a policy refusal over to Anthropic's default fallback model inside the same call.</summary>
    private static readonly string[] FallbackModels = { "claude-fable-5-1", "claude-opus-5-5", "claude-opus-5", "claude-sonnet-5-5" };
    private const string FallbackBeta = "server-side-fallback-2026-07-01";

    private readonly HttpClient _http;

    public AnthropicBackend(HttpClient http) => _http = http;

    public async Task<LlmResult> CompleteAsync(LlmSettings settings, LlmRequest request, CancellationToken ct)
    {
        string model = settings.ModelName;
        if (model.Length == 0) throw new LlmException("No Claude model is selected. Choose one in Settings.");

        var client = CreateClient(settings);
        var messages = request.Messages
            .Select(m => new BetaMessageParam { Role = m.Role == LlmRole.User ? Role.User : Role.Assistant, Content = m.Content })
            .ToList();

        var parameters = new MessageCreateParams
        {
            Model = model,
            MaxTokens = request.MaxOutputTokens ?? DefaultMaxTokens,
            Messages = messages
        };

        // The system block holds instructions and schema; the breakpoint makes every later request read it from the cache.
        if (request.System.Length > 0)
            parameters = parameters with { System = new List<BetaTextBlockParam> { new() { Text = request.System, CacheControl = new BetaCacheControlEphemeral() } } };

        // A database security review can look like offensive security work to a classifier; let the API re-serve
        // such a refusal with its default fallback model instead of failing the topic.
        if (UsesFallback(settings, model))
            parameters = parameters with { Betas = [FallbackBeta], Fallbacks = new Default() };

        BetaMessage message;
        try
        {
            message = await client.Beta.Messages.Create(parameters, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (Translate(ex, ct) is { } translated)
        {
            throw translated;
        }

        var text = new StringBuilder();
        foreach (var block in message.Content)
            if (block.TryPickText(out BetaTextBlock? t)) text.Append(t.Text);

        string? stop = message.StopReason?.Raw();
        if (stop == "refusal" && text.Length == 0)
        {
            string category = message.StopDetails?.Category?.Raw() is { Length: > 0 } c ? $" ({c})" : "";
            throw new LlmException($"Claude declined this request{category}. Rephrase the topic or try another model.");
        }

        var usage = message.Usage;
        long cacheRead = usage.CacheReadInputTokens ?? 0;
        long cacheWrite = usage.CacheCreationInputTokens ?? 0;
        // Anthropic reports uncached, cache-read and cache-write input separately; the total is their sum.
        var total = new LlmUsage(usage.InputTokens + cacheRead + cacheWrite, usage.OutputTokens, cacheRead);

        return new LlmResult { Text = text.ToString(), Usage = total, Truncated = stop == "max_tokens", Model = message.Model.Raw() };
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(LlmSettings settings, CancellationToken ct)
    {
        var client = CreateClient(settings);
        try
        {
            var page = await client.Models.List(new ModelListParams { Limit = 100 }, ct).ConfigureAwait(false);
            // The API lists the newest models first; keep that order.
            return page.Items.Select(m => m.ID).Where(id => id.StartsWith("claude", StringComparison.Ordinal)).Distinct().ToList();
        }
        catch (Exception ex) when (Translate(ex, ct) is { } translated)
        {
            throw translated;
        }
    }

    private AnthropicClient CreateClient(LlmSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new LlmException("The Anthropic API key is missing. Add it in Settings.");

        return new AnthropicClient
        {
            ApiKey = settings.ApiKey.Trim(),
            BaseUrl = HttpTransport.NormalizeBaseUrl(settings),
            HttpClient = _http,
            MaxRetries = 3,
            Timeout = HttpTransport.RequestTimeout
        };
    }

    private static bool UsesFallback(LlmSettings settings, string model) =>
        string.IsNullOrWhiteSpace(settings.BaseUrl) // first-party API only, not through proxies or gateways
        && FallbackModels.Contains(model, StringComparer.OrdinalIgnoreCase);

    /// <summary>Turns SDK exceptions into messages the user can act on; returns null for cancellation by the user.</summary>
    private static LlmException? Translate(Exception ex, CancellationToken ct)
    {
        if (ex is LlmException) return null;
        if (ex is OperationCanceledException && ct.IsCancellationRequested) return null;

        return ex switch
        {
            AnthropicUnauthorizedException or AnthropicForbiddenException =>
                new LlmException($"Claude request failed (access was denied — check the API key). {Detail(ex)}".Trim(), false, ex),
            AnthropicNotFoundException =>
                new LlmException($"Claude request failed (not found — check the model name). {Detail(ex)}".Trim(), false, ex),
            AnthropicRateLimitException =>
                new LlmException($"Claude request failed (rate limit reached). {Detail(ex)}".Trim(), true, ex),
            Anthropic5xxException =>
                new LlmException($"Claude is temporarily unavailable or overloaded. {Detail(ex)}".Trim(), true, ex),
            AnthropicApiException api =>
                new LlmException($"Claude request failed ({(int)api.StatusCode}). {Detail(ex)}".Trim(), HttpTransport.IsTransient(api.StatusCode), ex),
            AnthropicIOException =>
                new LlmException("Cannot reach the Anthropic API. Check your internet connection.", true, ex),
            OperationCanceledException or TimeoutException =>
                new LlmException($"The Claude request timed out after {HttpTransport.RequestTimeout.TotalMinutes:0} minutes.", true, ex),
            _ => new LlmException("Claude request failed: " + ex.Message, false, ex)
        };
    }

    private static string Detail(Exception ex) =>
        ex is AnthropicApiException api && HttpTransport.ParseError(api.ResponseBody ?? "") is { } message ? message : "";
}
