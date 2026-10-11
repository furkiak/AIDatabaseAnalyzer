using System.Net;

namespace AIDatabaseAnalyzer.Services.Llm;

/// <summary>The AI services the app can talk to. The numeric values are stored in preferences: never reorder.</summary>
public enum LlmProvider
{
    Ollama = 0,
    Gemini = 1,
    Claude = 2,
    OpenAI = 3,
    DeepSeek = 4,
    OpenAICompatible = 5
}

/// <summary>A snapshot of everything needed to talk to the selected AI model.</summary>
public sealed class LlmSettings
{
    public LlmProvider Provider { get; set; } = LlmProvider.Ollama;

    /// <summary>Model id; empty means the provider's default model.</summary>
    public string Model { get; set; } = "";

    public string ApiKey { get; set; } = "";

    /// <summary>Server address; empty means the provider's public endpoint.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>Context window for self-hosted models (Ollama, OpenAI-compatible servers). Larger values need much more memory.</summary>
    public int ContextWindow { get; set; } = 32768;

    /// <summary>How many requests may run at the same time against a cloud API.</summary>
    public int MaxParallelRequests { get; set; } = 3;

    public LlmProviderInfo Info => LlmProviders.Get(Provider);
    public string ProviderName => Info.Name;
    public string ModelName => string.IsNullOrWhiteSpace(Model) ? Info.DefaultModel : Model.Trim();
    public string EndpointUrl => string.IsNullOrWhiteSpace(BaseUrl) ? Info.DefaultBaseUrl : BaseUrl.Trim().TrimEnd('/');

    /// <summary>True when nothing leaves the user's machine or local network.</summary>
    public bool IsLocal => Provider == LlmProvider.Ollama || (Provider == LlmProvider.OpenAICompatible && IsPrivateAddress(EndpointUrl));

    /// <summary>Approximate number of prompt tokens the selected model can take.</summary>
    public int ContextTokens => Info.UserDefinedContext ? Math.Max(ContextWindow, 2048) : Info.ContextTokensFor(ModelName);

    /// <summary>Local models share one GPU, so they always run one request at a time.</summary>
    public int Parallelism => IsLocal ? 1 : Math.Clamp(MaxParallelRequests, 1, 8);

    public LlmSettings Clone() => (LlmSettings)MemberwiseClone();

    internal static bool IsPrivateAddress(string url)
    {
        if (!Uri.TryCreate(url.Contains("://", StringComparison.Ordinal) ? url : "http://" + url, UriKind.Absolute, out var uri)) return false;
        if (uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip)) return false;
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal) return true;
        byte[] b = ip.GetAddressBytes();
        return b.Length == 4 && (b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254));
    }
}

/// <summary>Supplies the current settings; implemented by the application's settings store.</summary>
public interface ILlmSettingsProvider
{
    LlmSettings GetLlmSettings();
}

public enum LlmRole
{
    User,
    Assistant
}

public sealed record LlmMessage(LlmRole Role, string Content);

/// <summary>
/// One request. <see cref="System"/> carries the large, stable part (instructions and schema) so that every provider can reuse
/// its processed prefix: Claude through an explicit cache breakpoint, the others through automatic prefix caching.
/// </summary>
public sealed class LlmRequest
{
    public string System { get; init; } = "";
    public IReadOnlyList<LlmMessage> Messages { get; init; } = Array.Empty<LlmMessage>();

    /// <summary>Upper limit for the answer; null lets the provider decide.</summary>
    public int? MaxOutputTokens { get; init; }

    public static LlmRequest Single(string system, string user, int? maxOutputTokens = null) =>
        new() { System = system, Messages = new[] { new LlmMessage(LlmRole.User, user) }, MaxOutputTokens = maxOutputTokens };

    /// <summary>Rough size in tokens (≈3.5 characters per token for schema-heavy text).</summary>
    public int EstimatedTokens => (int)((System.Length + Messages.Sum(m => m.Content.Length)) / 3.5);
}

/// <summary>Token usage as reported by the provider.</summary>
public readonly record struct LlmUsage(long InputTokens, long OutputTokens, long CachedInputTokens)
{
    public static LlmUsage operator +(LlmUsage a, LlmUsage b) =>
        new(a.InputTokens + b.InputTokens, a.OutputTokens + b.OutputTokens, a.CachedInputTokens + b.CachedInputTokens);

    public long TotalTokens => InputTokens + OutputTokens;

    /// <summary>Share of the input that was served from the provider's prompt cache.</summary>
    public double CacheRatio => InputTokens > 0 ? Math.Clamp((double)CachedInputTokens / InputTokens, 0, 1) : 0;

    public override string ToString() =>
        InputTokens == 0 && OutputTokens == 0
            ? ""
            : $"{Compact(InputTokens)} in · {Compact(OutputTokens)} out" + (CachedInputTokens > 0 ? $" · {CacheRatio:P0} cached" : "");

    public static string Compact(long tokens) =>
        tokens >= 1_000_000 ? $"{tokens / 1_000_000d:0.#}M" : tokens >= 1_000 ? $"{tokens / 1_000d:0.#}k" : tokens.ToString();
}

public sealed class LlmResult
{
    public string Text { get; init; } = "";
    public LlmUsage Usage { get; init; }

    /// <summary>The model stopped at its output limit; the answer may be incomplete.</summary>
    public bool Truncated { get; init; }

    /// <summary>The model that actually answered (may differ from the requested one after a fallback).</summary>
    public string Model { get; init; } = "";
}

public sealed class LlmTestResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public IReadOnlyList<string> Models { get; init; } = Array.Empty<string>();
}

/// <summary>An AI request failed. The message is safe to show to the user.</summary>
public sealed class LlmException : Exception
{
    public LlmException(string message, bool isTransient = false, Exception? inner = null) : base(message, inner) =>
        IsTransient = isTransient;

    public bool IsTransient { get; }
}
