using System.Net;
using System.Text;
using AIDatabaseAnalyzer.Models;
using AIDatabaseAnalyzer.Services.Database;
using AIDatabaseAnalyzer.Services.Insights;
using AIDatabaseAnalyzer.Services.Llm;
using AIDatabaseAnalyzer.Services.Reporting;

namespace AIDatabaseAnalyzer.Tests;

internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();
    public List<HttpRequestMessage> Requests { get; } = new();

    public FakeHandler Enqueue(HttpStatusCode status, string body, Action<HttpResponseMessage>? configure = null)
    {
        _responses.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            configure?.Invoke(response);
            return response;
        });
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var next = _responses.Count > 0 ? _responses.Dequeue() : throw new InvalidOperationException("No more fake responses queued.");
        return Task.FromResult(next(request));
    }
}

internal sealed class FixedSettings : ILlmSettingsProvider
{
    public LlmSettings Value { get; set; } = new();
    public LlmSettings GetLlmSettings() => Value;
}

public class LlmServiceTests
{
    private static (LlmService service, FakeHandler handler) Create()
    {
        var handler = new FakeHandler();
        return (new LlmService(new HttpClient(handler), new FixedSettings()), handler);
    }

    private static readonly LlmSettings Gemini = new() { Provider = LlmProvider.Gemini, GeminiApiKey = "SECRET-KEY", GeminiModel = "models/gemini-x" };

    [Fact]
    public async Task Gemini_answer_is_assembled_from_all_text_parts_and_the_key_stays_out_of_the_url()
    {
        var (service, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, """{"candidates":[{"content":{"parts":[{"text":"hidden","thought":true},{"text":"Hello"},{"text":" world"}]},"finishReason":"STOP"}]}""");

        string answer = await service.GenerateAsync(Gemini, "hi");

        Assert.Equal("Hello world", answer);
        var request = Assert.Single(handler.Requests);
        Assert.DoesNotContain("SECRET-KEY", request.RequestUri!.ToString());
        Assert.Equal("SECRET-KEY", request.Headers.GetValues("x-goog-api-key").Single());
        Assert.Contains("/models/gemini-x:generateContent", request.RequestUri.AbsolutePath);
    }

    [Fact]
    public async Task Rate_limited_requests_are_retried()
    {
        var (service, handler) = Create();
        handler.Enqueue((HttpStatusCode)429, """{"error":{"message":"slow down","status":"RESOURCE_EXHAUSTED"}}""", r => r.Headers.RetryAfter = new(TimeSpan.Zero))
               .Enqueue(HttpStatusCode.OK, """{"candidates":[{"content":{"parts":[{"text":"ok"}]}}]}""");

        Assert.Equal("ok", await service.GenerateAsync(Gemini, "hi"));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Errors_explain_what_went_wrong()
    {
        var (service, handler) = Create();
        handler.Enqueue(HttpStatusCode.Forbidden, """{"error":{"message":"API key not valid","status":"PERMISSION_DENIED"}}""");

        var ex = await Assert.ThrowsAsync<LlmException>(() => service.GenerateAsync(Gemini, "hi"));
        Assert.Contains("PERMISSION_DENIED", ex.Message);
        Assert.Contains("API key", ex.Message);
    }

    [Fact]
    public async Task Blocked_prompts_give_a_clear_message()
    {
        var (service, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, """{"promptFeedback":{"blockReason":"SAFETY"}}""");

        var ex = await Assert.ThrowsAsync<LlmException>(() => service.GenerateAsync(Gemini, "hi"));
        Assert.Contains("blocked", ex.Message);
    }

    [Fact]
    public async Task Ollama_refuses_prompts_that_do_not_fit_the_context_window()
    {
        var (service, _) = Create();
        var settings = new LlmSettings { OllamaContext = 2048 };

        var ex = await Assert.ThrowsAsync<LlmException>(() => service.GenerateAsync(settings, new string('x', 20_000)));
        Assert.Contains("context window", ex.Message);
    }

    [Fact]
    public async Task Ollama_reports_missing_models_with_the_install_command()
    {
        var (service, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, """{"models":[{"name":"llama3.1:8b"}]}""");

        var result = await service.TestAsync(new LlmSettings { OllamaModel = "qwen2.5-coder" });
        Assert.False(result.Success);
        Assert.Contains("ollama pull qwen2.5-coder", result.Message);
        Assert.Equal(new[] { "llama3.1:8b" }, result.Models);
    }

    [Fact]
    public async Task Gemini_model_list_only_contains_text_generation_models()
    {
        var (service, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, """
            {"models":[
              {"name":"models/gemini-2.5-flash","supportedGenerationMethods":["generateContent"]},
              {"name":"models/gemini-embedding-001","supportedGenerationMethods":["embedContent"]},
              {"name":"models/gemini-2.5-flash-image","supportedGenerationMethods":["generateContent"]}]}
            """);

        var models = await service.ListModelsAsync(Gemini);
        Assert.Equal(new[] { "gemini-2.5-flash" }, models);
    }
}

internal sealed class FakeLlm : ILlmService
{
    public Func<string, string> Answer { get; set; } = _ => "answer";
    public int Calls { get; private set; }

    public Task<string> GenerateAsync(string prompt, CancellationToken ct = default) => GenerateAsync(new LlmSettings(), prompt, ct);

    public Task<string> GenerateAsync(LlmSettings settings, string prompt, CancellationToken ct = default)
    {
        Calls++;
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Answer(prompt));
    }

    public Task<LlmTestResult> TestAsync(LlmSettings settings, CancellationToken ct = default) => Task.FromResult(new LlmTestResult());
    public Task<IReadOnlyList<string>> ListModelsAsync(LlmSettings settings, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
}

public class AnalysisRunnerTests
{
    private static AnalysisRequest Request(params AnalysisTopic[] topics)
    {
        var schema = TestData.FlawedShop();
        return new AnalysisRequest
        {
            Schema = schema,
            SchemaJson = SchemaSerializer.ToJson(schema, false),
            Insights = SchemaInsightsService.Analyze(schema),
            Topics = topics
        };
    }

    private static AnalysisTopic Topic(string title) => new() { Title = title, Prompt = $"Question about {title}", Category = "Design" };

    [Fact]
    public async Task Schema_comes_first_in_every_prompt_so_providers_can_cache_the_prefix()
    {
        var prompts = new List<string>();
        var llm = new FakeLlm { Answer = p => { prompts.Add(p); return "ok"; } };

        await new AnalysisRunner(llm).RunAsync(Request(Topic("A"), Topic("B")), new LlmSettings(), null, null, CancellationToken.None);

        string prefixA = prompts[0][..prompts[0].IndexOf("## Task", StringComparison.Ordinal)];
        string prefixB = prompts[1][..prompts[1].IndexOf("## Task", StringComparison.Ordinal)];
        Assert.Equal(prefixA, prefixB);
        Assert.Contains("<schema>", prefixA);
    }

    [Fact]
    public async Task A_failing_topic_does_not_stop_the_run_and_can_be_retried()
    {
        bool fail = true;
        var llm = new FakeLlm { Answer = p => p.Contains("Question about Bad") && fail ? throw new LlmException("boom") : "fine" };
        var runner = new AnalysisRunner(llm);
        var request = Request(Topic("Good"), Topic("Bad"));

        var first = await runner.RunAsync(request, new LlmSettings(), null, null, CancellationToken.None);
        Assert.Equal(1, first.FailedSections);
        Assert.Contains(first.Sections, s => s.Title.EndsWith("Executive Summary"));

        fail = false;
        int callsBefore = llm.Calls;
        var second = await runner.RunAsync(request, new LlmSettings(), first, null, CancellationToken.None);

        Assert.Equal(0, second.FailedSections);
        Assert.Equal(1, llm.Calls - callsBefore); // only the failed topic was asked again; summary and plan were reused
    }

    [Fact]
    public async Task Cancelling_returns_what_was_completed_without_a_summary()
    {
        using var cts = new CancellationTokenSource();
        var llm = new FakeLlm { Answer = _ => { cts.Cancel(); return "done"; } };

        var report = await new AnalysisRunner(llm).RunAsync(Request(Topic("A"), Topic("B"), Topic("C")), new LlmSettings(), null, null, cts.Token);

        Assert.Single(report.Sections);
        Assert.Equal("A", report.Sections[0].Title);
    }

    [Fact]
    public void Digest_stays_within_its_budget_and_skips_failed_sections()
    {
        var sections = Enumerable.Range(0, 40).Select(i => new ReportSection { Title = $"S{i}", Content = new string('x', 5000) }).ToList();
        sections.Add(new ReportSection { Title = "Broken", Content = "error", IsError = true });

        string digest = PromptBuilder.BuildDigest(sections, 20_000);
        Assert.InRange(digest.Length, 1, 40 * 400 + 5000);
        Assert.DoesNotContain("Broken", digest);
    }
}

public class ReportTests
{
    private static AnalysisReport Report()
    {
        var report = new AnalysisReport { DatabaseName = "<b>shop</b>", EngineName = "PostgreSQL", Provider = "Test", Model = "m", TableCount = 3, HealthScore = 80, HealthGrade = "B" };
        report.Findings.Add(new ReportFinding { Severity = "High", Title = "No PK", Description = "d", Recommendation = "r", Objects = { "audit_log" } });
        report.Sections.Add(new ReportSection { Title = "1. Summary", Content = "# Heading\n\n- **item** ✅ ş ğ İ\n\n| a | b |\n|---|---|\n| 1 | 2 |" });
        return report;
    }

    [Fact]
    public void Html_report_escapes_titles_and_renders_markdown()
    {
        string html = ReportRenderer.ToHtml(Report());
        Assert.DoesNotContain("<b>shop</b>", html);
        Assert.Contains("&lt;b&gt;shop&lt;/b&gt;", html);
        Assert.Contains("<table>", html);
        Assert.Contains("<strong>item</strong>", html);
    }

    [Fact]
    public void Markdown_report_contains_findings_and_sections()
    {
        string md = ReportRenderer.ToMarkdown(Report());
        Assert.Contains("[High] No PK", md);
        Assert.Contains("## 1. Summary", md);
    }

    [Fact]
    public void Pdf_report_is_generated_even_with_emoji_and_non_latin_text()
    {
        byte[] pdf = new PdfReportService().ToBytes(Report());
        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
    }
}
