using AIDatabaseAnalyzer.Models;
using AIDatabaseAnalyzer.Services.Database;
using AIDatabaseAnalyzer.Services.Insights;

namespace AIDatabaseAnalyzer.Services.Llm;

/// <summary>What happened to one topic; reported to the UI while a run is in progress.</summary>
public sealed record AnalysisProgress(AnalysisTopic? Topic, AnalysisStatus Status, int Completed, int Total, string Message, string? Error = null);

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
    /// Runs every topic one after another, then writes the executive summary and the action plan.
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

    private readonly ILlmService _llm;

    public AnalysisRunner(ILlmService llm) => _llm = llm;

    public async Task<AnalysisReport> RunAsync(
        AnalysisRequest request,
        LlmSettings settings,
        AnalysisReport? previous,
        IProgress<AnalysisProgress>? progress,
        CancellationToken ct)
    {
        var topics = request.Topics;
        int total = topics.Count + 2;
        int completed = 0;

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
            .ToDictionary(s => s.Title, StringComparer.Ordinal);

        string context = PromptBuilder.SchemaContext(request.Schema, request.SchemaJson);
        var topicSections = new List<ReportSection>();
        bool cancelled = false;

        foreach (var topic in topics)
        {
            if (cancelled || ct.IsCancellationRequested)
            {
                cancelled = true;
                progress?.Report(new AnalysisProgress(topic, AnalysisStatus.Cancelled, completed, total, "Cancelled"));
                continue;
            }

            if (reusable.TryGetValue(topic.Title, out var existing))
            {
                topicSections.Add(existing);
                completed++;
                progress?.Report(new AnalysisProgress(topic, AnalysisStatus.Completed, completed, total, $"Reused: {topic.Title}"));
                continue;
            }

            progress?.Report(new AnalysisProgress(topic, AnalysisStatus.Processing, completed, total, topic.Title));
            try
            {
                string answer = await _llm.GenerateAsync(settings, PromptBuilder.TopicPrompt(context, topic, request.Language), ct).ConfigureAwait(false);
                topicSections.Add(new ReportSection { Title = topic.Title, Category = topic.Category, Content = answer.Trim() });
                completed++;
                progress?.Report(new AnalysisProgress(topic, AnalysisStatus.Completed, completed, total, topic.Title));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                cancelled = true;
                progress?.Report(new AnalysisProgress(topic, AnalysisStatus.Cancelled, completed, total, "Cancelled"));
            }
            catch (Exception ex)
            {
                topicSections.Add(new ReportSection { Title = topic.Title, Category = topic.Category, IsError = true, Content = "**This topic could not be analysed.**\n\n" + ex.Message });
                completed++;
                progress?.Report(new AnalysisProgress(topic, AnalysisStatus.Error, completed, total, topic.Title, ex.Message));
            }
        }

        // Executive summary and action plan are only worth writing when there is something to summarise.
        var sections = new List<ReportSection>();
        if (!cancelled && topicSections.Any(s => !s.IsError))
        {
            int budget = settings.IsLocal ? Math.Min(24_000, (int)(settings.ContextTokens * 3.5 * 0.45)) : 40_000;
            string digest = PromptBuilder.BuildDigest(topicSections, budget);
            string header = PromptBuilder.SchemaHeader(request.Schema);

            sections.Add(await TryGenerateAsync(SummaryTitle, "Summary", PromptBuilder.SummaryPrompt(header, digest, request.Insights, request.Language),
                previous, settings, ct, progress, () => completed, () => ++completed, total));
            sections.Add(await TryGenerateAsync(ActionPlanTitle, "Summary", PromptBuilder.ActionPlanPrompt(header, digest, request.Insights, request.Language),
                previous, settings, ct, progress, () => completed, () => ++completed, total));
        }

        sections.AddRange(topicSections);
        for (int i = 0; i < sections.Count; i++)
            sections[i].Title = sections[i].Category == "Summary" ? $"{i + 1}. {sections[i].Title}" : sections[i].Title;

        report.Sections = sections;
        progress?.Report(new AnalysisProgress(null, cancelled ? AnalysisStatus.Cancelled : AnalysisStatus.Completed, total, total,
            cancelled ? "Cancelled" : "Analysis complete"));
        return report;
    }

    private async Task<ReportSection> TryGenerateAsync(
        string title, string category, string prompt, AnalysisReport? previous, LlmSettings settings,
        CancellationToken ct, IProgress<AnalysisProgress>? progress, Func<int> current, Func<int> advance, int total)
    {
        // Titles carry a "1. " prefix in saved reports, compare without it.
        var existing = previous?.Sections.FirstOrDefault(s => !s.IsError && s.Category == category && StripNumber(s.Title) == title);
        if (existing is not null)
        {
            existing.Title = title;
            advance();
            return existing;
        }

        progress?.Report(new AnalysisProgress(null, AnalysisStatus.Processing, current(), total, title));
        try
        {
            string answer = await _llm.GenerateAsync(settings, prompt, ct).ConfigureAwait(false);
            advance();
            return new ReportSection { Title = title, Category = category, Content = answer.Trim() };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            advance();
            return new ReportSection { Title = title, Category = category, IsError = true, Content = "**This section could not be generated.**\n\n" + ex.Message };
        }
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
}
