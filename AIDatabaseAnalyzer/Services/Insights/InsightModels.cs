namespace AIDatabaseAnalyzer.Services.Insights;

public enum FindingSeverity
{
    Info = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public static class FindingCategory
{
    public const string Design = "Design";
    public const string Performance = "Performance";
    public const string Security = "Security";
    public const string Integrity = "Integrity";
    public const string Maintainability = "Maintainability";
}

/// <summary>One result of the offline (rule based) schema analysis.</summary>
public sealed class Finding
{
    public required string RuleId { get; init; }
    public required FindingSeverity Severity { get; init; }
    public required string Category { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Recommendation { get; init; }

    /// <summary>Affected objects, already shortened to a displayable amount.</summary>
    public IReadOnlyList<string> Objects { get; init; } = Array.Empty<string>();

    /// <summary>Total number of affected objects (can be larger than <see cref="Objects"/>).</summary>
    public int Count { get; init; }

    /// <summary>Full names of every affected table (not shortened), used to colour and filter the ER diagram.</summary>
    public IReadOnlyList<string> Tables { get; init; } = Array.Empty<string>();
}

public sealed class SchemaInsights
{
    public int Score { get; init; }
    public string Grade { get; init; } = "";
    public IReadOnlyList<Finding> Findings { get; init; } = Array.Empty<Finding>();
    public IReadOnlyList<string> SkippedChecks { get; init; } = Array.Empty<string>();

    public int Count(FindingSeverity severity) => Findings.Count(f => f.Severity == severity);

    public string Headline => Score switch
    {
        >= 90 => "Excellent — the schema follows almost every best practice we check.",
        >= 80 => "Good — a few improvements will make it even more robust.",
        >= 70 => "Fair — several design and performance issues deserve attention.",
        >= 55 => "Needs work — important structural problems were detected.",
        _ => "Critical — significant risks in design, integrity or performance."
    };
}
