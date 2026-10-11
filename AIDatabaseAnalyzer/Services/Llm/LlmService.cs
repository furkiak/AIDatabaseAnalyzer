using System.Text.RegularExpressions;
using AIDatabaseAnalyzer.Services.Llm.Backends;

namespace AIDatabaseAnalyzer.Services.Llm;

/// <summary>Routes requests to the selected provider and post-processes the answers the same way for all of them.</summary>
public sealed partial class LlmService : ILlmService
{
    private const string TruncationNote = "\n\n*The model stopped because it reached its output limit; the answer may be incomplete.*";

    private readonly HttpTransport _transport;
    private readonly OllamaBackend _ollama;
    private readonly GeminiBackend _gemini;
    private readonly OpenAiBackend _openAi;
    private readonly AnthropicBackend _anthropic;

    public LlmService(HttpClient http)
    {
        // Timeouts are handled per request so that long local generations are not cut off by the client default.
        http.Timeout = Timeout.InfiniteTimeSpan;
        _transport = new HttpTransport(http);
        _ollama = new OllamaBackend(_transport);
        _gemini = new GeminiBackend(_transport);
        _openAi = new OpenAiBackend(_transport);
        _anthropic = new AnthropicBackend(http);
    }

    /// <summary>Replaces the back-off delay between retries (tests).</summary>
    internal Func<TimeSpan, CancellationToken, Task> RetryDelay
    {
        get => _transport.Delay;
        set => _transport.Delay = value;
    }

    private ILlmBackend Backend(LlmProvider provider) => provider switch
    {
        LlmProvider.Ollama => _ollama,
        LlmProvider.Gemini => _gemini,
        LlmProvider.Claude => _anthropic,
        _ => _openAi
    };

    public async Task<LlmResult> CompleteAsync(LlmSettings settings, LlmRequest request, CancellationToken ct = default)
    {
        if (request.Messages.Count == 0 || request.Messages[^1].Role != LlmRole.User)
            throw new ArgumentException("A request must end with a user message.", nameof(request));

        var result = await Backend(settings.Provider).CompleteAsync(settings, request, ct).ConfigureAwait(false);

        string text = StripReasoning(result.Text).Trim();
        if (text.Length == 0)
            throw new LlmException($"{settings.ProviderName} returned an empty answer. Try again or choose another model.", isTransient: true);
        if (result.Truncated) text += TruncationNote;

        return new LlmResult { Text = text, Usage = result.Usage, Truncated = result.Truncated, Model = result.Model };
    }

    public Task<IReadOnlyList<string>> ListModelsAsync(LlmSettings settings, CancellationToken ct = default) =>
        Backend(settings.Provider).ListModelsAsync(settings, ct);

    public async Task<LlmTestResult> TestAsync(LlmSettings settings, CancellationToken ct = default)
    {
        string provider = settings.ProviderName;
        string wanted = settings.Provider == LlmProvider.Gemini ? GeminiBackend.StripModelPrefix(settings.ModelName) : settings.ModelName;

        IReadOnlyList<string> models;
        try
        {
            models = await ListModelsAsync(settings, ct).ConfigureAwait(false);
        }
        catch (LlmException ex) when (settings.Provider == LlmProvider.OpenAICompatible && ex.Message.Contains("(404", StringComparison.Ordinal))
        {
            // Some gateways do not implement /models: fall back to a tiny request.
            return await PingAsync(settings, ct).ConfigureAwait(false);
        }
        catch (LlmException ex)
        {
            return new LlmTestResult { Success = false, Message = ex.Message };
        }

        if (wanted.Length == 0)
            return new LlmTestResult { Success = false, Models = models, Message = $"Connected to {provider} ({models.Count} models available). Pick a model from the list." };

        bool found = models.Any(m => string.Equals(m, wanted, StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(m, wanted + ":latest", StringComparison.OrdinalIgnoreCase)
                                     // aliases such as "claude-haiku-4-5" resolve to dated ids
                                     || m.StartsWith(wanted + "-", StringComparison.OrdinalIgnoreCase));
        if (found)
            return new LlmTestResult { Success = true, Models = models, Message = $"{provider} is ready — model “{wanted}” is available." };

        if (models.Count == 0 && settings.Provider == LlmProvider.OpenAICompatible)
            return await PingAsync(settings, ct).ConfigureAwait(false);

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

    private async Task<LlmTestResult> PingAsync(LlmSettings settings, CancellationToken ct)
    {
        try
        {
            var result = await CompleteAsync(settings, LlmRequest.Single("", "Reply with the single word OK.", 64), ct).ConfigureAwait(false);
            return new LlmTestResult { Success = true, Message = $"{settings.ProviderName} answered with model “{result.Model}”." };
        }
        catch (LlmException ex)
        {
            return new LlmTestResult { Success = false, Message = ex.Message };
        }
    }

    /// <summary>Reasoning models served through Ollama or compatible servers put their chain of thought in &lt;think&gt; tags.</summary>
    internal static string StripReasoning(string text)
    {
        if (text.Length == 0) return text;
        string cleaned = ThinkBlock().Replace(text, "");
        // An unterminated block at the very start means the model was cut off while thinking.
        var open = OpenThink().Match(cleaned);
        if (open.Success && open.Index == 0) cleaned = "";
        return cleaned;
    }

    [GeneratedRegex(@"<(think|thinking|reasoning)>.*?</\1>\s*", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ThinkBlock();

    [GeneratedRegex(@"^\s*<(think|thinking|reasoning)>", RegexOptions.IgnoreCase)]
    private static partial Regex OpenThink();
}
