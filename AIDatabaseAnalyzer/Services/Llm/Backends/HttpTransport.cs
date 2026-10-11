using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AIDatabaseAnalyzer.Services.Llm.Backends;

/// <summary>Sends JSON requests with retries, exponential back-off (honouring Retry-After) and readable errors.</summary>
internal sealed class HttpTransport
{
    private const int MaxAttempts = 4;
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(10);

    private readonly HttpClient _http;

    public HttpTransport(HttpClient http) => _http = http;

    public HttpClient Client => _http;

    /// <summary>Called instead of <see cref="Task.Delay(TimeSpan, CancellationToken)"/>; tests replace it to run instantly.</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;

    public static HttpRequestMessage Json(HttpMethod method, string url, string? json, Action<HttpRequestHeaders>? headers = null)
    {
        var request = new HttpRequestMessage(method, url);
        headers?.Invoke(request.Headers);
        if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    public async Task<string> SendAsync(
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
            catch (HttpRequestException ex) when (IsDroppedConnection(ex) && attempt < MaxAttempts && !ct.IsCancellationRequested)
            {
                // A pooled keep-alive connection was closed by the server or a proxy: simply try again.
                await Delay(TimeSpan.FromMilliseconds(250 * attempt), ct).ConfigureAwait(false);
                continue;
            }
            catch (HttpRequestException ex)
            {
                throw new LlmException(unreachableMessage, isTransient: IsDroppedConnection(ex), ex);
            }

            if ((int)status is >= 200 and < 300) return body;

            bool transient = IsTransient(status);
            if (transient && attempt < MaxAttempts)
            {
                var delay = retryAfter ?? TimeSpan.FromSeconds(Math.Pow(2, attempt) * 1.5 + Random.Shared.NextDouble());
                if (delay > TimeSpan.FromSeconds(60)) delay = TimeSpan.FromSeconds(60);
                await Delay(delay, ct).ConfigureAwait(false);
                continue;
            }

            string detail = parseError(body) ?? Shorten(body);
            throw new LlmException($"{what} failed ({(int)status} {Friendly(status, reason)}). {detail}".Trim(), transient);
        }
    }

    private static bool IsDroppedConnection(HttpRequestException ex) =>
        ex.HttpRequestError is HttpRequestError.ResponseEnded or HttpRequestError.InvalidResponse
        || (ex.HttpRequestError == HttpRequestError.Unknown && ex.InnerException is IOException);

    public static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout
        || (int)status == 529; // "overloaded"

    public static string Friendly(HttpStatusCode status, string reason) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "access was denied — check the API key",
        HttpStatusCode.NotFound => "not found — check the model name and the address",
        HttpStatusCode.TooManyRequests => "rate limit or quota reached",
        HttpStatusCode.PaymentRequired => "the account has no credit left",
        (HttpStatusCode)529 => "the service is overloaded",
        _ => reason
    };

    /// <summary>Reads <c>{"error": {"message": …}}</c> (OpenAI, DeepSeek, Gemini, Anthropic) or <c>{"error": "…"}</c> (Ollama).</summary>
    public static string? ParseError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("error", out var error)) return null;
            if (error.ValueKind == JsonValueKind.String) return error.GetString();
            if (error.ValueKind != JsonValueKind.Object) return null;

            string? message = error.TryGetProperty("message", out var m) ? m.GetString() : null;
            string? code = error.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString()
                : error.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()
                : null;
            return code is null || message?.Contains(code, StringComparison.OrdinalIgnoreCase) == true ? message : $"{code}: {message}";
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Validates a user-entered server address.</summary>
    public static string NormalizeBaseUrl(LlmSettings settings)
    {
        string url = settings.EndpointUrl;
        if (url.Length == 0) throw new LlmException($"Enter the {settings.ProviderName} server address in Settings.");
        if (!url.Contains("://", StringComparison.Ordinal)) url = "http://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            throw new LlmException($"“{settings.EndpointUrl}” is not a valid address (example: {settings.Info.DefaultBaseUrl}).");
        return url.TrimEnd('/');
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

    public static string Shorten(string text) => text.Length <= 300 ? text.Trim() : text[..300].Trim() + "…";
}
