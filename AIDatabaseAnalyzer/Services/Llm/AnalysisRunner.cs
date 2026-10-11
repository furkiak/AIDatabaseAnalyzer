using System.Diagnostics;
using AIDatabaseAnalyzer.Models;
using AIDatabaseAnalyzer.Services.Database;
using AIDatabaseAnalyzer.Services.Insights;

namespace AIDatabaseAnalyzer.Services.Llm;

/// <summary>What happened to one topic; reported to the UI while a run is in progress.</summary>
public sealed record AnalysisProgress(
    AnalysisTopic? Topic, AnalysisStatus Status, int Completed, int Total, string Message, string? Error = null, LlmUsage Usage = default);

public sealed class AnalysisRequest
{
    public required DatabaseSchema Schema { get; init; }
    public required string SchemaJson { get; init; }
    public SchemaInsights? Insights { get; init; }
    public required IReadOnlyList<AnalysisTopic> Topics { get; init; }
    public string Language { get; init; } = "English";
}

public interface IAnalysisRunner
{
    /// <summary>
    /// Answers every topic (several at a time for cloud providers), then writes the executive summary and the action plan.
    /// Sections of <paramref name="previous"/> that already succeeded are reused, so the same call also retries failed topics.
    /// </summary>
    Task<AnalysisReport> RunAsync(
        AnalysisRequest request,
        LlmSettings settings,
        AnalysisReport? previous,
        IProgress<AnalysisProgress>? progress,
        CancellationToken ct);
}

public sealed class AnalysisRunner : IAnalysisRunner
{
    public const string SummaryTitle = "Executive Summary";
    public const string ActionPlanTitle = "Prioritized Action Plan";
    private const string SummaryCategory = "Summary";

    private readonly ILlmService _llm;

    public AnalysisRunner(ILlmService llm) => _llm = llm;

    public async Task<AnalysisReport> RunAsync(
        AnalysisRequest request,
        LlmSettings settings,
        AnalysisReport? previous,
        IProgress<AnalysisProgress>? progress,
        CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        var run = new RunState(request.Topics.Count + 2, progress);

        var report = new AnalysisReport
        {
            DatabaseName = request.Schema.DatabaseName,
            EngineName = request.Schema.Engine.DisplayName(),
            Provider = settings.ProviderName,
            Model = settings.ModelName,
            Language = request.Language,
            TableCount = request.Schema.Tables.Count,
            HealthScore = request.Insights?.Score ?? 0,
            HealthGrade = request.Insights?.Grade ?? "",
            Findings = ToReportFindings(request.Insights)
        };

        var reusable = (previous?.Sections ?? new List<ReportSection>())
            .Where(s => !s.IsError)
            .GroupBy(s => s.Title, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        string system = PromptBuilder.SystemPrompt(request.Schema, request.SchemaJson, request.Insights, request.Language);
        var topics = request.Topics;
        var results = new ReportSection?[topics.Count];
        var pending = new List<int>();

        for (int i = 0; i < topics.Count; i++)
        {
            if (reusable.TryGetValue(topics[i].Title, out var existing))
            {
                results[i] = existing;
                run.Done(topics[i], AnalysisStatus.Completed, $"Reused: {topics[i].Title}");
            }
            else
            {
                pending.Add(i);
            }
        }

        async Task AnswerAsync(int index)
        {
            var topic = topics[index];
            if (ct.IsCancellationRequested)
            {
                run.Cancelled(topic);
                return;
            }

            run.Report(topic, AnalysisStatus.Processing, topic.Title);
            try
            {
                var answer = await CompleteWithRetryAsync(settings, LlmRequest.Single(system, PromptBuilder.TopicTask(topic)), ct).ConfigureAwait(false);
                run.Add(answer.Usage);
                results[index] = new ReportSection { Title = topic.Title, Category = topic.Category, Content = answer.Text };
                run.Done(topic, AnalysisStatus.Completed, topic.Title);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                run.Cancelled(topic);
            }
            catch (Exception ex)
            {
                results[index] = new ReportSection { Title = topic.Title, Category = topic.Category, IsError = true, Content = "**This topic could not be analysed.**\n\n" + ex.Message };
                run.Done(topic, AnalysisStatus.Error, topic.Title, ex.Message);
            }
        }

        if (pending.Count > 0)
        {
            // The first request writes the provider's prompt cache; the others then read the schema from it.
            await AnswerAsync(pending[0]).ConfigureAwait(false);
            await ForEachAsync(pending.Skip(1), settings.Parallelism, AnswerAsync, ct).ConfigureAwait(false);
        }

        bool cancelled = ct.IsCancellationRequested;
        var topicSections = results.Where(s => s is not null).Select(s => s!).ToList();

        // Executive summary and action plan are only worth writing when there is something to summarise.
        var sections = new List<ReportSection>();
        if (!cancelled && topicSections.Any(s => !s.IsError))
        {
            int budget = settings.IsLocal ? Math.Min(24_000, (int)(settings.ContextTokens * 3.5 * 0.45)) : 60_000;
            string digest = PromptBuilder.BuildDigest(topicSections, budget);

            // Re-use the cached schema prompt when it comfortably fits; local models get a compact one.
            int fullTokens = (int)((system.Length + digest.Length) / 3.5) + 6_000;
            string summarySystem = !settings.IsLocal && fullTokens < settings.ContextTokens
                ? system
                : PromptBuilder.CompactSystemPrompt(request.Schema, request.Insights, request.Language);

            var finals = new ReportSection?[2];
            var jobs = new (string Title, string Task)[]
            {
                (SummaryTitle, PromptBuilder.SummaryTask(digest)),
                (ActionPlanTitle, PromptBuilder.ActionPlanTask(digest))
            };

            await ForEachAsync(Enumerable.Range(0, jobs.Length), Math.Min(2, settings.Parallelism), async i =>
            {
                finals[i] = await SummariseAsync(jobs[i].Title, LlmRequest.Single(summarySystem, jobs[i].Task), previous, settings, run, ct).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);

            cancelled = ct.IsCancellationRequested;
            sections.AddRange(finals.Where(s => s is not null).Select(s => s!));
        }

        sections.AddRange(topicSections);
        for (int i = 0; i < sections.Count; i++)
            if (sections[i].Category == SummaryCategory) sections[i].Title = $"{i + 1}. {StripNumber(sections[i].Title)}";

        report.Sections = sections;
        report.InputTokens = run.Usage.InputTokens;
        report.OutputTokens = run.Usage.OutputTokens;
        report.CachedInputTokens = run.Usage.CachedInputTokens;
        report.DurationSeconds = Math.Round(clock.Elapsed.TotalSeconds, 1);

        progress?.Report(new AnalysisProgress(null, cancelled ? AnalysisStatus.Cancelled : AnalysisStatus.Completed, run.Total, run.Total,
            cancelled ? "Cancelled" : "Analysis complete", Usage: run.Usage));
        return report;
    }

    private async Task<ReportSection?> SummariseAsync(
        string title, LlmRequest request, AnalysisReport? previous, LlmSettings settings, RunState run, CancellationToken ct)
    {
        // Titles carry a "1. " prefix in saved reports, compare without it.
        var existing = previous?.Sections.FirstOrDefault(s => !s.IsError && s.Category == SummaryCategory && StripNumber(s.Title) == title);
        if (existing is not null)
        {
            run.Done(null, AnalysisStatus.Completed, title);
            return new ReportSection { Title = title, Category = SummaryCategory, Content = existing.Content };
        }

        if (ct.IsCancellationRequested) return null;
        run.Report(null, AnalysisStatus.Processing, title);
        try
        {
            var answer = await CompleteWithRetryAsync(settings, request, ct).ConfigureAwait(false);
            run.Add(answer.Usage);
            run.Done(null, AnalysisStatus.Completed, title);
            return new ReportSection { Title = title, Category = SummaryCategory, Content = answer.Text };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            run.Done(null, AnalysisStatus.Error, title, ex.Message);
            return new ReportSection { Title = title, Category = SummaryCategory, IsError = true, Content = "**This section could not be generated.**\n\n" + ex.Message };
        }
    }

    /// <summary>One more attempt for answers that were empty, cut off by a time-out or rejected by an overloaded service.</summary>
    private async Task<LlmResult> CompleteWithRetryAsync(LlmSettings settings, LlmRequest request, CancellationToken ct)
    {
        try
        {
            return await _llm.CompleteAsync(settings, request, ct).ConfigureAwait(false);
        }
        catch (LlmException ex) when (ex.IsTransient && !ct.IsCancellationRequested)
        {
            return await _llm.CompleteAsync(settings, request, ct).ConfigureAwait(false);
        }
    }

    private static async Task ForEachAsync<T>(IEnumerable<T> items, int parallelism, Func<T, Task> body, CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(Math.Max(1, parallelism));
        var tasks = items.Select(async item =>
        {
            try
            {
                await gate.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await body(item).ConfigureAwait(false); // reports the item as cancelled
                return;
            }

            try
            {
                await body(item).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }).ToList();
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static string StripNumber(string title)
    {
        int dot = title.IndexOf(". ", StringComparison.Ordinal);
        return dot is > 0 and <= 2 && title[..dot].All(char.IsDigit) ? title[(dot + 2)..] : title;
    }

    private static List<ReportFinding> ToReportFindings(SchemaInsights? insights) =>
        insights is null
            ? new List<ReportFinding>()
            : insights.Findings
                .Where(f => f.Severity >= FindingSeverity.Low)
                .Select(f => new ReportFinding
                {
                    Severity = f.Severity.ToString(),
                    Category = f.Category,
                    Title = f.Title,
                    Description = f.Description,
                    Recommendation = f.Recommendation,
                    Objects = f.Objects.ToList()
                }).ToList();

    /// <summary>Thread-safe progress counter and usage total for one run.</summary>
    private sealed class RunState
    {
        private readonly object _lock = new();
        private readonly IProgress<AnalysisProgress>? _progress;
        private int _completed;
        private LlmUsage _usage;

        public RunState(int total, IProgress<AnalysisProgress>? progress)
        {
            Total = total;
            _progress = progress;
        }

        public int Total { get; }

        public LlmUsage Usage
        {
            get { lock (_lock) return _usage; }
        }

        public void Add(LlmUsage usage)
        {
            lock (_lock) _usage += usage;
        }

        public void Report(AnalysisTopic? topic, AnalysisStatus status, string message, string? error = null)
        {
            int completed;
            LlmUsage usage;
            lock (_lock)
            {
                completed = _completed;
                usage = _usage;
            }
            _progress?.Report(new AnalysisProgress(topic, status, completed, Total, message, error, usage));
        }

        public void Done(AnalysisTopic? topic, AnalysisStatus status, string message, string? error = null)
        {
            lock (_lock) _completed++;
            Report(topic, status, message, error);
        }

        public void Cancelled(AnalysisTopic topic) => Report(topic, AnalysisStatus.Cancelled, "Cancelled");
    }
}
