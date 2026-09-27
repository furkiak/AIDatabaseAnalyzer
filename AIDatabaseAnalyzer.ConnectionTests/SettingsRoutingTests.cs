using System.Net;
using System.Text;
using System.Text.Json;
using AIDatabaseAnalyzer.Services;

namespace AIDatabaseAnalyzer.ConnectionTests;

// Synthetic HttpMessageHandler evidence exercises production request construction, not a live model.
public sealed class SettingsRoutingTests : IDisposable
{
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "OwnedRoutingFixtures", Guid.NewGuid().ToString("N"));
    private AppProfile Profile => AppProfile.Resolve(["--data-directory", Path.Combine(_root, "isolated")], Path.Combine(_root, "normal"));

    [Fact]
    public async Task ProductionOllamaPathReloadsRecipientAndModelWhileAppearancePreservesBoth()
    {
        var profile = Profile; var store = new IsolatedSettingsStore(profile);
        store.Set("OllamaUrl", "http://127.0.0.1:11434"); store.Set("OllamaModel", "synthetic-first");
        using var handler = new CaptureHandler("{\"response\":\"synthetic handler response\"}");
        using var client = new HttpClient(handler); var service = new LlmService(client, store);
        using var cancellation = new CancellationTokenSource();
        await service.AskOllamaAsync("owned fixture prompt", cancellation.Token);
        Assert.Equal("/api/generate", handler.Uri!.AbsolutePath);
        Assert.Equal("synthetic-first", JsonDocument.Parse(handler.Body!).RootElement.GetProperty("model").GetString());
        store.Set("OllamaUrl", "http://localhost:11435"); store.Set("OllamaModel", "synthetic-second");
        new AppearancePreferencesStore(profile).Save(new("Dark", 200));
        await service.AskOllamaAsync("owned next prompt", cancellation.Token);
        Assert.Equal("localhost", handler.Uri!.Host); Assert.Equal(11435, handler.Uri.Port);
        using var request = JsonDocument.Parse(handler.Body!);
        Assert.Equal("synthetic-second", request.RootElement.GetProperty("model").GetString());
        Assert.False(request.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal(16384, request.RootElement.GetProperty("options").GetProperty("num_ctx").GetInt32());
        Assert.Equal(TimeSpan.FromMinutes(10), client.Timeout);
        Assert.True(handler.CancellationToken.CanBeCanceled);
    }

    [Fact, Trait("Category", "WindowsProfile")]
    public async Task ProductionGeminiPathUsesOnlyIsolatedDpapiKeyInHeaderAfterAppearanceSave()
    {
        Assert.True(OperatingSystem.IsWindows());
        var profile = Profile; var store = new IsolatedSettingsStore(profile);
        store.Set("GeminiModel", "synthetic-gemini-model");
        const string secret = "owned-synthetic-api-key";
        await store.SetSecretAsync("GeminiApiKey", secret);
        new AppearancePreferencesStore(profile).Save(new("Dark", 150));
        using var handler = new CaptureHandler("{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"synthetic handler response\"}]}}]}");
        using var client = new HttpClient(handler); var service = new LlmService(client, store);
        Assert.Equal("synthetic handler response", await service.AskGeminiAsync("owned prompt"));
        Assert.Equal(secret, handler.ApiKey);
        Assert.Contains("synthetic-gemini-model", handler.Uri!.AbsolutePath);
        Assert.DoesNotContain(secret, handler.Uri.ToString()); Assert.DoesNotContain(secret, handler.Body!);
        Assert.False(Directory.Exists(Path.Combine(_root, "normal")));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    [InlineData("42")]
    public async Task ProductionOllamaRejectsMissingUsableAnswerInsteadOfReturningSuccess(string answerJson)
    {
        var store = new IsolatedSettingsStore(Profile);
        using var handler = new CaptureHandler("{\"response\":" + answerJson + "}");
        using var client = new HttpClient(handler);
        var service = new LlmService(client, store);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.AskOllamaAsync("owned response fixture"));
    }

    [Theory, Trait("Category", "WindowsProfile")]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    [InlineData("42")]
    public async Task ProductionGeminiRejectsMissingUsableAnswerInsteadOfReturningSuccess(string answerJson)
    {
        var store = new IsolatedSettingsStore(Profile);
        await store.SetSecretAsync("GeminiApiKey", "owned-synthetic-response-key");
        using var handler = new CaptureHandler("{\"candidates\":[{\"content\":{\"parts\":[{\"text\":" + answerJson + "}]}}]}");
        using var client = new HttpClient(handler);
        var service = new LlmService(client, store);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.AskGeminiAsync("owned response fixture"));
    }

    private sealed class CaptureHandler(string response) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public string? Body { get; private set; }
        public string? ApiKey { get; private set; }
        public CancellationToken CancellationToken { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri; Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            ApiKey = request.Headers.TryGetValues("x-goog-api-key", out var values) ? values.Single() : null;
            CancellationToken = cancellationToken;
            return new(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }

    public void Dispose()
    {
        string ownedRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "OwnedRoutingFixtures")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(_root).StartsWith(ownedRoot, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid cleanup target.");
        AppProfile.AssertNoLinks(_root);
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
