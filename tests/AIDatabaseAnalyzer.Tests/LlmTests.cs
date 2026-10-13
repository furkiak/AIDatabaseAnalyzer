using System.Net;
using System.Text;
using System.Text.Json;
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

    /// <summary>Request bodies, captured before the caller disposes the request.</summary>
    public List<string> Bodies { get; } = new();

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

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
        var next = _responses.Count > 0 ? _responses.Dequeue() : throw new InvalidOperationException("No more fake responses queued.");
        return next(request);
    }
}

public class LlmServiceTests
{
    private static (LlmService service, FakeHandler handler) Create()
    {
        var handler = new FakeHandler();
        return (new LlmService(new HttpClient(handler)) { RetryDelay = (_, _) => Task.CompletedTask }, handler);
    }

    private static readonly LlmSettings Gemini = new() { Provider = LlmProvider.Gemini, ApiKey = "SECRET-KEY", Model = "models/gemini-x" };
    private static readonly LlmSettings OpenAI = new() { Provider = LlmProvider.OpenAI, ApiKey = "sk-test", Model = "gpt-5-mini" };
    private static readonly LlmSettings Claude = new() { Provider = LlmProvider.Claude, ApiKey = "sk-ant-test" };

    private static LlmRequest Ask(string user = "hi", string system = "You review databases.") => LlmRequest.Single(system, user);

    private static JsonDocument BodyOf(FakeHandler handler, int index = 0) => JsonDocument.Parse(handler.Bodies[index]);

    [Fact]
    public async Task Gemini_answer_is_assembled_from_all_text_parts_and_the_key_stays_out_of_the_url()
    {
        var (service, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, """{"candidates":[{"content":{"parts":[{"text":"hidden","thought":true},{"text":"Hello"},{"text":" world"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":100,"candidatesTokenCount":5,"cachedContentTokenCount":60}}""");

        var answer = await service.CompleteAsync(Gemini, Ask());

        Assert.Equal("Hello world", answer.Text);
        Assert.Equal(new LlmUsage(100, 5, 60), answer.Usage);
        var request = Assert.Single(handler.Requests);
        Assert.DoesNotContain("SECRET-KEY", request.RequestUri!.ToString());
        Assert.Equal("SECRET-KEY", request.Headers.GetValues("x-goog-api-key").Single());
        Assert.Contains("/models/gemini-x:generateContent", request.RequestUri.AbsolutePath);
        using var body = BodyOf(handler);
        Assert.Equal("You review databases.", body.RootElement.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Rate_limited_requests_are_retried()
    {
        var (service, handler) = Create();
        handler.Enqueue((HttpStatusCode)429, """{"error":{"message":"slow down","status":"RESOURCE_EXHAUSTED"}}""", r => r.Headers.RetryAfter = new(TimeSpan.Zero))
               .Enqueue(HttpStatusCode.OK, """{"candidates":[{"content":{"parts":[{"text":"ok"}]}}]}""");

        Assert.Equal("ok", (await service.CompleteAsync(Gemini, Ask())).Text);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Errors_explain_what_went_wrong()
    {
        var (service, handler) = Create();
        handler.Enqueue(HttpStatusCode.Forbidden, """{"error":{"message":"API key not valid","status":"PERMISSION_DENIED"}}""");

        var ex = await Assert.ThrowsAsync<LlmException>(() => service.CompleteAsync(Gemini, Ask()));
        Assert.Contains("PERMISSION_DENIED", ex.Message);
        Assert.Contains("API key", ex.Message);
    }

    [Fact]
    public async Task Blocked_prompts_give_a_clear_message()
    {
        var (service, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, """{"promptFeedback":{"blockReason":"SAFETY"}}""");

        var ex = await Assert.ThrowsAsync<LlmException>(() => service.CompleteAsync(Gemini, Ask()));
        Assert.Contains("blocked", ex.Message);
    }

    [Fact]
    public async Task Ollama_uses_the_chat_api_with_a_system_message()
    {
        var (service, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, """{"message":{"role":"assistant","content":"<think>let me see</think>Answer"},"done_reason":"stop","prompt_eval_count":40,"eval_count":3}""");

        var answer = await service.CompleteAsync(new LlmSettings { BaseUrl = "localhost:11434" }, Ask());

        Assert.Equal("Answer", answer.Text);
        Assert.Equal(new LlmUsage(40, 3, 0), answer.Usage);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://localhost:11434/api/chat", request.RequestUri!.ToString());
        using var body = BodyOf(handler);
        Assert.Equal("system", body.RootElement.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal("qwen2.5-coder", body.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task Ollama_refuses_prompts_that_do_not_fit_the_context_window()
    {
        var (service, _) = Create();
        var settings = new LlmSettings { ContextWindow = 2048 };

        var ex = await Assert.ThrowsAsync<LlmException>(() => service.CompleteAsync(settings, Ask(new string('x', 20_000))));
        Assert.Contains("context window", ex.Message);
    }

    [Fact]
    public async Task Ollama_reports_missing_models_with_the_install_command()
    {
        var (service, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, """{"models":[{"name":"llama3.1:8b"}]}""");

        var result = await service.TestAsync(new LlmSettings { Model = "qwen2.5-coder" });
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

    [Fact]
    public async Task OpenAI_sends_a_bearer_token_and_reads_cached_usage()
    {
        var (service, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, """{"model":"gpt-5-mini-2025","choices":[{"message":{"role":"assistant","content":"Fine"},"finish_reason":"length"}],"usage":{"prompt_tokens":2000,"completion_tokens":10,"prompt_tokens_details":{"cached_tokens":1536}}}""");

        var answer = await service.CompleteAsync(OpenAI, Ask());

        Assert.StartsWith("Fine", answer.Text);
        Assert.True(answer.Truncated);
        Assert.Contains("output limit", answer.Text);
        Assert.Equal(new LlmUsage(2000, 10, 1536), answer.Usage);
        Assert.Equal("gpt-5-mini-2025", answer.Model);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.openai.com/v1/chat/completions", request.RequestUri!.ToString());
        Assert.Equal("Bearer sk-test", request.Headers.Authorization!.ToString());
        using var body = BodyOf(handler);
        Assert.False(body.RootElement.TryGetProperty("temperature", out _)); // reasoning models reject custom temperatures
        Assert.Equal("system", body.RootElement.GetProperty("messages")[0].GetProperty("role").GetString());
    }

    [Fact]
    public async Task DeepSeek_asks_for_a_larger_answer_and_reads_its_cache_counter()
    {
        var (service, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, """{"choices":[{"message":{"content":"ok"},"finish_reason":"stop"}],"usage":{"prompt_tokens":900,"completion_tokens":4,"prompt_cache_hit_tokens":640,"prompt_cache_miss_tokens":260}}""");

        var answer = await service.CompleteAsync(new LlmSettings { Provider = LlmProvider.DeepSeek, ApiKey = "k" }, Ask());

        Assert.Equal(640, answer.Usage.CachedInputTokens);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.deepseek.com/chat/completions", request.RequestUri!.ToString());
        using var body = BodyOf(handler);
        Assert.Equal("deepseek-chat", body.RootElement.GetProperty("model").GetString());
        Assert.Equal(8192, body.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task OpenAI_model_list_hides_embedding_audio_and_image_models()
    {
        var (service, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, """{"data":[{"id":"gpt-5"},{"id":"text-embedding-3-small"},{"id":"gpt-4o-realtime-preview"},{"id":"dall-e-3"},{"id":"o3"},{"id":"whisper-1"}]}""");

        Assert.Equal(new[] { "gpt-5", "o3" }, await service.ListModelsAsync(OpenAI));
    }

    [Fact]
    public async Task Compatible_servers_work_without_a_key_and_count_as_local_on_private_addresses()
    {
        var (service, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, """{"choices":[{"message":{"content":"hello"}}]}""");
        var settings = new LlmSettings { Provider = LlmProvider.OpenAICompatible, BaseUrl = "http://192.168.1.20:1234/v1/", Model = "qwen3" };

        Assert.Equal("hello", (await service.CompleteAsync(settings, Ask())).Text);
        Assert.Null(handler.Requests[0].Headers.Authorization);
        Assert.Equal("http://192.168.1.20:1234/v1/chat/completions", handler.Requests[0].RequestUri!.ToString());
        Assert.True(settings.IsLocal);
        Assert.Equal(1, settings.Parallelism);
        Assert.False(new LlmSettings { Provider = LlmProvider.OpenAICompatible, BaseUrl = "https://openrouter.ai/api/v1" }.IsLocal);
    }

    [Fact]
    public async Task Claude_caches_the_system_prompt_and_reports_cache_reads()
    {
        var (service, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, """{"id":"msg_1","type":"message","role":"assistant","model":"claude-opus-5-5","content":[{"type":"thinking","thinking":"","signature":"s"},{"type":"text","text":"Schema looks fine."}],"stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":20,"output_tokens":7,"cache_read_input_tokens":4000,"cache_creation_input_tokens":0}}""");

        var answer = await service.CompleteAsync(Claude, Ask());

        Assert.Equal("Schema looks fine.", answer.Text);
        Assert.Equal(new LlmUsage(4020, 7, 4000), answer.Usage);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.anthropic.com/v1/messages", request.RequestUri!.GetLeftPart(UriPartial.Path));
        Assert.Equal("sk-ant-test", request.Headers.GetValues("x-api-key").Single());
        using var body = BodyOf(handler);
        var system = body.RootElement.GetProperty("system")[0];
        Assert.Equal("You review databases.", system.GetProperty("text").GetString());
        Assert.Equal("ephemeral", system.GetProperty("cache_control").GetProperty("type").GetString());
        Assert.Equal("claude-opus-5-5", body.RootElement.GetProperty("model").GetString());
        // refusals of the default model are re-served by its fallback model
        Assert.Equal("default", body.RootElement.GetProperty("fallbacks").GetString());
        Assert.Contains("server-side-fallback", string.Join(",", request.Headers.GetValues("anthropic-beta")));
        Assert.False(body.RootElement.TryGetProperty("temperature", out _));
    }

    [Fact]
    public async Task Claude_refusals_and_bad_keys_have_readable_messages()
    {
        var (service, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, """{"id":"m","type":"message","role":"assistant","model":"claude-opus-5-5","content":[],"stop_reason":"refusal","stop_details":{"type":"refusal","category":"cyber","explanation":"x"},"stop_sequence":null,"usage":{"input_tokens":5,"output_tokens":0}}""")
               .Enqueue(HttpStatusCode.Unauthorized, """{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}""");

        var refusal = await Assert.ThrowsAsync<LlmException>(() => service.CompleteAsync(Claude, Ask()));
        Assert.Contains("declined", refusal.Message);
        Assert.Contains("cyber", refusal.Message);

        var denied = await Assert.ThrowsAsync<LlmException>(() => service.CompleteAsync(Claude, Ask()));
        Assert.Contains("API key", denied.Message);
        Assert.False(denied.IsTransient);
    }

    [Fact]
    public async Task Claude_test_accepts_aliases_of_dated_model_ids()
    {
        var (service, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, """{"data":[{"id":"claude-haiku-4-5-20251001","display_name":"Claude Haiku 4.5","created_at":"2025-10-01T00:00:00Z","type":"model"}],"has_more":false,"first_id":"a","last_id":"a"}""");

        var result = await service.TestAsync(new LlmSettings { Provider = LlmProvider.Claude, ApiKey = "k", Model = "claude-haiku-4-5" });
        Assert.True(result.Success, result.Message);
    }

    [Fact]
    public async Task Missing_keys_are_reported_before_any_request()
    {
        var settings = new LlmSettings { Provider = LlmProvider.OpenAI };
        var ex = await Assert.ThrowsAsync<LlmException>(() => Create().service.CompleteAsync(settings, Ask()));
        Assert.Contains("API key", ex.Message);
    }

    [Theory]
    [InlineData("<think>a\nb</think>\n\nResult", "Result")]
    [InlineData("Plain answer", "Plain answer")]
    [InlineData("<thinking>never closed", "")]
    public void Reasoning_blocks_are_removed(string input, string expected) =>
        Assert.Equal(expected, LlmService.StripReasoning(input).Trim());

    [Theory]
    [InlineData("claude-opus-5-5", 1_000_000)]
    [InlineData("claude-sonnet-4-6", 1_000_000)]
    [InlineData("claude-haiku-4-5", 200_000)]
    [InlineData("claude-sonnet-4-20250514", 200_000)]
    public void Claude_context_window_depends_on_the_generation(string model, int expected) =>
        Assert.Equal(expected, new LlmSettings { Provider = LlmProvider.Claude, Model = model }.ContextTokens);
}

internal sealed class FakeLlm : ILlmService
{
    private int _calls;
    public Func<LlmRequest, string> Answer { get; set; } = _ => "answer";
    public int Calls => _calls;
    public List<LlmRequest> Requests { get; } = new();

    public async Task<LlmResult> CompleteAsync(LlmSettings settings, LlmRequest request, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _calls);
        lock (Requests) Requests.Add(request);
        ct.ThrowIfCancellationRequested();
        await Task.Yield(); // like a real HTTP call, continue asynchronously
        return new LlmResult { Text = Answer(request), Usage = new LlmUsage(100, 10, 80) };
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

    private static string User(LlmRequest r) => r.Messages[^1].Content;

    [Fact]
    public async Task Every_topic_shares_one_system_prompt_so_providers_can_cache_the_schema()
    {
        var llm = new FakeLlm();

        await new AnalysisRunner(llm).RunAsync(Request(Topic("A"), Topic("B")), new LlmSettings(), null, null, CancellationToken.None);

        var topicRequests = llm.Requests.Where(r => User(r).Contains("Question about")).ToList();
        Assert.Equal(2, topicRequests.Count);
        Assert.Equal(topicRequests[0].System, topicRequests[1].System);
        Assert.Contains("<schema>", topicRequests[0].System);
        Assert.Contains("Automated checks", topicRequests[0].System);
        Assert.DoesNotContain("<schema>", User(topicRequests[0]));
    }

    [Fact]
    public async Task A_failing_topic_does_not_stop_the_run_and_can_be_retried()
    {
        bool fail = true;
        var llm = new FakeLlm { Answer = r => User(r).Contains("Question about Bad") && fail ? throw new LlmException("boom") : "fine" };
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
        Assert.Equal("1. Executive Summary", second.Sections[0].Title);
        Assert.Equal("2. Prioritized Action Plan", second.Sections[1].Title);
    }

    [Fact]
    public async Task Transient_errors_get_one_more_attempt()
    {
        int attempts = 0;
        var llm = new FakeLlm { Answer = r => User(r).Contains("Question about Flaky") && ++attempts == 1 ? throw new LlmException("overloaded", isTransient: true) : "ok" };

        var report = await new AnalysisRunner(llm).RunAsync(Request(Topic("Flaky")), new LlmSettings(), null, null, CancellationToken.None);

        Assert.Equal(0, report.FailedSections);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Cloud_runs_answer_several_topics_at_once_and_keep_their_order()
    {
        int running = 0, peak = 0;
        var llm = new FakeLlm
        {
            Answer = r =>
            {
                int now = Interlocked.Increment(ref running);
                InterlockedMax(ref peak, now);
                Thread.Sleep(60);
                Interlocked.Decrement(ref running);
                return "answer for " + User(r);
            }
        };
        var settings = new LlmSettings { Provider = LlmProvider.OpenAI, ApiKey = "k", MaxParallelRequests = 3 };
        var topics = Enumerable.Range(1, 7).Select(i => Topic("T" + i)).ToArray();

        var report = await new AnalysisRunner(llm).RunAsync(Request(topics), settings, null, null, CancellationToken.None);

        Assert.InRange(peak, 2, 3);
        Assert.Equal(topics.Select(t => t.Title), report.Sections.Where(s => s.Category == "Design").Select(s => s.Title));
        Assert.Equal(9 * 100, report.InputTokens);
        Assert.Equal(9 * 80, report.CachedInputTokens);
        Assert.Contains("from cache", report.UsageText);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, current) != current) { }
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
