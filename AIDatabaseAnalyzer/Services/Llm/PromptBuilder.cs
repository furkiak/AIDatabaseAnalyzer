using System.Text;
using AIDatabaseAnalyzer.Models;
using AIDatabaseAnalyzer.Services.Database;
using AIDatabaseAnalyzer.Services.Insights;

namespace AIDatabaseAnalyzer.Services.Llm;

/// <summary>Builds prompts so that every request shares the same (large) prefix: the schema comes first, the task last.
/// Both Ollama and Gemini reuse the already processed prefix, which makes dozens of topics far faster and cheaper.</summary>
public static class PromptBuilder
{
    public static readonly string[] Languages = { "English", "Türkçe" };

    public static string LanguageInstruction(string language) =>
        language.StartsWith("T", StringComparison.OrdinalIgnoreCase) ? "Turkish (Türkçe)" : "English";

    /// <summary>The part of the prompt that is identical for every topic.</summary>
    public static string SchemaContext(DatabaseSchema schema, string schemaJson)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"You are a senior database architect and DBA reviewing the {schema.Engine.DisplayName()} database \"{schema.DatabaseName}\".");
        sb.AppendLine("The schema is given as compact JSON:");
        sb.AppendLine("- \"Tables\": table name -> { \"Rows\": estimated row count, \"Columns\": [\"name type [PK] [IDENTITY] NULL|NOT NULL [DEFAULT x]\"] }");
        sb.AppendLine("- \"ForeignKeys\", \"Indexes\", \"Constraints\" (CHECK) as one-line strings; \"Triggers\", \"Views\", \"StoredProcedures\" with optional SQL \"Definition\"");
        sb.AppendLine("- \"SampleData\": a few real rows per table; values may be shortened or masked for privacy.");
        sb.AppendLine();
        sb.AppendLine("<schema>");
        sb.AppendLine(schemaJson);
        sb.AppendLine("</schema>");
        return sb.ToString();
    }

    public static string TopicPrompt(string schemaContext, AnalysisTopic topic, string language) =>
        $"""
        {schemaContext}
        ## Task
        {topic.Prompt}

        ## Output rules
        - Write the answer in {LanguageInstruction(language)}.
        - Use Markdown with short headings, bullet lists and tables where they help.
        - Refer to real table, column and index names from the schema. Never invent objects.
        - Give every finding a severity (Critical, High, Medium, Low) and a concrete fix (SQL for the {SqlFlavor(schemaContext)} engine when useful).
        - If the schema does not contain enough information for this task, say so briefly instead of guessing.
        - Be concise: no introduction, do not repeat the task.
        """;

    public static string SummaryPrompt(string schemaHeader, string digest, SchemaInsights? insights, string language) =>
        $"""
        {schemaHeader}

        Below are the condensed results of an automated review (offline checks and expert analyses by topic).

        {FindingsDigest(insights)}
        {digest}

        ## Task
        Write an **Executive Summary** for management and the technical lead: the overall health of the database, the 5 most important risks (with severity and business impact) and the biggest strengths.
        Keep it under 400 words. Write in {LanguageInstruction(language)} using Markdown. Do not invent facts that are not in the results.
        """;

    public static string ActionPlanPrompt(string schemaHeader, string digest, SchemaInsights? insights, string language) =>
        $"""
        {schemaHeader}

        Below are the condensed results of an automated review (offline checks and expert analyses by topic).

        {FindingsDigest(insights)}
        {digest}

        ## Task
        Create a **Prioritized Action Plan**: a Markdown table with the columns Priority (P0–P3), Action, Affected objects, Effort (S/M/L) and Expected benefit,
        followed by a short suggested order of execution (quick wins first, risky changes last).
        Write in {LanguageInstruction(language)}. Merge duplicates and do not invent objects that are not in the results.
        """;

    public static string SchemaHeader(DatabaseSchema schema) =>
        $"Database \"{schema.DatabaseName}\" ({schema.Engine.DisplayName()}): {schema.Tables.Count} tables, {schema.ColumnCount} columns, {schema.ForeignKeys.Count} foreign keys.";

    private static string FindingsDigest(SchemaInsights? insights)
    {
        if (insights is null || insights.Findings.Count == 0) return "";
        var sb = new StringBuilder();
        sb.AppendLine($"### Automated checks (health score {insights.Score}/100)");
        foreach (var f in insights.Findings.Where(f => f.Severity >= FindingSeverity.Low).Take(20))
            sb.AppendLine($"- [{f.Severity}] {f.Title} ({f.Count}): {string.Join(", ", f.Objects.Take(4))}");
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>Shares a character budget evenly between all answers so the summary prompt always fits the context window.</summary>
    public static string BuildDigest(IReadOnlyList<ReportSection> sections, int totalChars)
    {
        var good = sections.Where(s => !s.IsError && !string.IsNullOrWhiteSpace(s.Content)).ToList();
        if (good.Count == 0) return "";

        int perSection = Math.Clamp(totalChars / good.Count, 300, 2500);
        var sb = new StringBuilder();
        foreach (var s in good)
        {
            string text = s.Content.Trim();
            if (text.Length > perSection) text = text[..perSection] + " …";
            sb.AppendLine($"### {s.Title}");
            sb.AppendLine(text);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string SqlFlavor(string schemaContext) =>
        schemaContext.Contains("SQL Server", StringComparison.Ordinal) ? "SQL Server (T-SQL)"
        : schemaContext.Contains("PostgreSQL", StringComparison.Ordinal) ? "PostgreSQL"
        : schemaContext.Contains("MySQL", StringComparison.Ordinal) ? "MySQL / MariaDB"
        : "database";
}
