namespace AIDatabaseAnalyzer.Models;

/// <summary>One chapter of a report (an AI answer or the executive summary).</summary>
public sealed class ReportSection
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string Category { get; set; } = "";
    public bool IsError { get; set; }
}

/// <summary>A condensed offline finding stored with the report.</summary>
public sealed class ReportFinding
{
    public string Severity { get; set; } = "";
    public string Category { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Recommendation { get; set; } = "";
    public List<string> Objects { get; set; } = new();
}

/// <summary>A finished analysis. Persisted as JSON so reports can be re-opened and re-exported later.</summary>
public sealed class AnalysisReport
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DatabaseName { get; set; } = "";
    public string EngineName { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public string Language { get; set; } = "English";
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;

    public int TableCount { get; set; }
    public int HealthScore { get; set; }
    public string HealthGrade { get; set; } = "";

    public List<ReportFinding> Findings { get; set; } = new();
    public List<ReportSection> Sections { get; set; } = new();

    /// <summary>Tokens reported by the provider for the whole run (0 for reports created by older versions).</summary>
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CachedInputTokens { get; set; }
    public double DurationSeconds { get; set; }

    public string Title => $"{DatabaseName} — Analysis Report";
    public int FailedSections => Sections.Count(s => s.IsError);

    public string UsageText
    {
        get
        {
            if (InputTokens == 0 && OutputTokens == 0) return "";
            static string K(long t) => t >= 1_000_000 ? $"{t / 1_000_000d:0.#}M" : t >= 1_000 ? $"{t / 1_000d:0.#}k" : t.ToString();
            string cached = CachedInputTokens > 0 && InputTokens > 0 ? $" ({(double)CachedInputTokens / InputTokens:P0} from cache)" : "";
            string time = DurationSeconds > 0 ? $" · {TimeSpan.FromSeconds(DurationSeconds):m\\:ss} min" : "";
            return $"{K(InputTokens)} input{cached} · {K(OutputTokens)} output tokens{time}";
        }
    }
}
