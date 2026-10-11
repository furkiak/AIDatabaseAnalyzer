using System.Text;
using AIDatabaseAnalyzer.Models;
using AIDatabaseAnalyzer.Services.Insights;

namespace AIDatabaseAnalyzer.Services.Llm;

/// <summary>
/// Builds prompts so that every request of a run shares the same (large) system prompt: instructions and schema first,
/// the task last. Claude caches that prefix explicitly; OpenAI, DeepSeek, Gemini and Ollama reuse it automatically.
/// </summary>
public static class PromptBuilder
{
    public static readonly string[] Languages = { "English", "Türkçe" };

    public static string LanguageInstruction(string language) =>
        language.StartsWith("T", StringComparison.OrdinalIgnoreCase) ? "Turkish (Türkçe)" : "English";

    /// <summary>The SQL dialect the model should write.</summary>
    public static string SqlDialect(DbEngine engine) => engine switch
    {
        DbEngine.SqlServer => "SQL Server (T-SQL)",
        DbEngine.PostgreSql => "PostgreSQL",
        DbEngine.MySql => "MySQL / MariaDB",
        DbEngine.Sqlite => "SQLite",
        DbEngine.Oracle => "Oracle (SQL and PL/SQL)",
        _ => "standard SQL"
    };

    private static string EngineNotes(DbEngine engine) => engine switch
    {
        DbEngine.Sqlite => "- SQLite specifics: there are no stored procedures, users or schemas; ALTER TABLE is limited (adding constraints needs a table rebuild); column types are affinities, so declared lengths are not enforced; foreign keys only work with PRAGMA foreign_keys = ON.",
        DbEngine.Oracle => "- Oracle specifics: use VARCHAR2/NUMBER/TIMESTAMP types, identity columns or sequences, and PL/SQL blocks; DDL commits implicitly; empty strings are NULL.",
        DbEngine.MySql => "- MySQL specifics: assume InnoDB; mention when advice differs between MySQL 8 and MariaDB.",
        DbEngine.PostgreSql => "- PostgreSQL specifics: prefer CREATE INDEX CONCURRENTLY and other online variants for large tables.",
        DbEngine.SqlServer => "- SQL Server specifics: mention ONLINE = ON (Enterprise / Azure SQL) for operations on large tables.",
        _ => ""
    };

    public static string SchemaHeader(DatabaseSchema schema)
    {
        string version = string.IsNullOrWhiteSpace(schema.ServerVersion) ? "" : $", version {schema.ServerVersion.Split('\n')[0].Trim()}";
        return $"Database \"{schema.DatabaseName}\" ({schema.Engine.DisplayName()}{version}): {schema.Tables.Count} tables, {schema.ColumnCount} columns, {schema.ForeignKeys.Count} foreign keys.";
    }

    /// <summary>
    /// The part of every request that stays the same during a run: role, answer rules, schema and the offline findings.
    /// </summary>
    public static string SystemPrompt(DatabaseSchema schema, string schemaJson, SchemaInsights? insights, string language)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"You are a senior database architect and DBA reviewing a {schema.Engine.DisplayName()} database.");
        sb.AppendLine(SchemaHeader(schema));
        sb.AppendLine("Each request contains one review task about this database. Answer only that task.");
        sb.AppendLine();
        AppendRules(sb, schema.Engine, language);
        sb.AppendLine();
        sb.AppendLine("## Schema format");
        sb.AppendLine("The schema is compact JSON:");
        sb.AppendLine("- \"Tables\": table name -> { \"Rows\": estimated row count, \"Columns\": [\"name type [PK] [IDENTITY] NULL|NOT NULL [DEFAULT x]\"] }");
        sb.AppendLine("- \"ForeignKeys\", \"Indexes\", \"Constraints\" (CHECK) as one-line strings; \"Triggers\", \"Views\", \"StoredProcedures\" with optional SQL \"Definition\"");
        sb.AppendLine("- \"SampleData\": a few real rows per table; values may be shortened or masked for privacy.");
        sb.AppendLine("Sections that were not extracted are missing from the JSON.");
        sb.AppendLine();
        sb.AppendLine("<schema>");
        sb.AppendLine(schemaJson);
        sb.AppendLine("</schema>");
        sb.Append(FindingsDigest(insights));
        return sb.ToString();
    }

    /// <summary>System prompt of the schema chat: the same schema block as the analysis, with conversational answer rules.</summary>
    public static string ChatSystemPrompt(DatabaseSchema schema, string schemaJson, SchemaInsights? insights)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"You are a senior database architect and SQL expert. You answer questions about one {schema.Engine.DisplayName()} database.");
        sb.AppendLine(SchemaHeader(schema));
        sb.AppendLine();
        sb.AppendLine("## How to answer");
        sb.AppendLine("- Answer in the language of the user's question, in concise Markdown.");
        sb.AppendLine($"- When the user asks for a query or a change, write correct SQL for {SqlDialect(schema.Engine)} in ```sql blocks, using only tables and columns that exist in the schema, joined along the foreign keys.");
        sb.AppendLine("- Write object names exactly as they appear in the schema, in backticks (for example `Orders.CustomerId`), so the app can link them to the ER diagram.");
        sb.AppendLine("- Prefer read-only queries; when you suggest a change (DDL or DML), warn about locks, data loss and the need for a backup or a test copy.");
        sb.AppendLine("- If the schema does not contain what the question needs, say so instead of guessing. Sample values may be masked.");
        string notes = EngineNotes(schema.Engine);
        if (notes.Length > 0) sb.AppendLine(notes);
        sb.AppendLine();
        sb.AppendLine("<schema>");
        sb.AppendLine(schemaJson);
        sb.AppendLine("</schema>");
        sb.Append(FindingsDigest(insights));
        return sb.ToString();
    }

    /// <summary>A small system prompt without the schema, for summaries when the context window is tight (local models).</summary>
    public static string CompactSystemPrompt(DatabaseSchema schema, SchemaInsights? insights, string language)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"You are a senior database architect and DBA writing the conclusion of a review of a {schema.Engine.DisplayName()} database.");
        sb.AppendLine(SchemaHeader(schema));
        sb.AppendLine();
        AppendRules(sb, schema.Engine, language);
        sb.Append(FindingsDigest(insights));
        return sb.ToString();
    }

    private static void AppendRules(StringBuilder sb, DbEngine engine, string language)
    {
        sb.AppendLine("## How to answer");
        sb.AppendLine($"- Write in {LanguageInstruction(language)}.");
        sb.AppendLine("- Use Markdown with short headings, bullet lists and tables where they help. No introduction; do not repeat the task.");
        sb.AppendLine("- Refer only to objects that exist in the schema and write their names exactly as they appear there, in backticks (for example `dbo.Orders` or `Orders.CustomerId`). Never invent tables, columns or indexes.");
        sb.AppendLine($"- Give every finding a severity (Critical, High, Medium or Low) and a concrete fix. Write SQL for {SqlDialect(engine)} when it helps, in ```sql blocks.");
        sb.AppendLine("- If the schema does not contain what the task needs (for example no sample rows or no code definitions were extracted), say so in one sentence instead of guessing.");
        string notes = EngineNotes(engine);
        if (notes.Length > 0) sb.AppendLine(notes);
    }

    public static string TopicTask(AnalysisTopic topic) =>
        $"""
        ## Task: {topic.Title}
        {topic.Prompt}
        """;

    public static string SummaryTask(string digest) =>
        $"""
        Below are the condensed results of the review (expert analyses by topic). The automated findings are in the system prompt.

        {digest}

        ## Task
        Write an **Executive Summary** for management and the technical lead: the overall health of the database, the 5 most important risks (with severity and business impact) and the biggest strengths.
        Keep it under 400 words. Do not invent facts that are not in the results.
        """;

    public static string ActionPlanTask(string digest) =>
        $"""
        Below are the condensed results of the review (expert analyses by topic). The automated findings are in the system prompt.

        {digest}

        ## Task
        Create a **Prioritized Action Plan**: a Markdown table with the columns Priority (P0–P3), Action, Affected objects, Effort (S/M/L) and Expected benefit,
        followed by a short suggested order of execution (quick wins first, risky changes last).
        Merge duplicates and do not invent objects that are not in the results.
        """;

    private static string FindingsDigest(SchemaInsights? insights)
    {
        if (insights is null || insights.Findings.Count == 0) return "";
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine($"## Automated checks (health score {insights.Score}/100, grade {insights.Grade})");
        sb.AppendLine("An offline rule engine already reported the findings below. Build on them, confirm or refute them with evidence, and prioritise; do not merely repeat them.");
        foreach (var f in insights.Findings.Where(f => f.Severity >= FindingSeverity.Low).Take(25))
            sb.AppendLine($"- [{f.Severity}] {f.Title} ({f.Count}): {string.Join(", ", f.Objects.Take(5))}{(f.Objects.Count > 5 ? ", …" : "")}");
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
}
