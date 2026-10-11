using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AIDatabaseAnalyzer.Models;
using AIDatabaseAnalyzer.Services.Database;
using AIDatabaseAnalyzer.Services.Insights;

namespace AIDatabaseAnalyzer.Services.Reporting;

/// <summary>What the diagram can show besides the schema: health findings, AI report mentions and a table or finding to focus.</summary>
public sealed class DiagramContext
{
    public SchemaInsights? Insights { get; init; }

    /// <summary>The latest AI report of the same database; tables mentioned in it link back to its sections.</summary>
    public AnalysisReport? Report { get; init; }

    public string? FocusTable { get; init; }
    public string? FocusRule { get; init; }

    /// <summary>Start with the health lens switched on (tables coloured by their worst finding).</summary>
    public bool HealthLens { get; init; }
}

/// <summary>
/// Builds the self-contained interactive ER diagram page (no external scripts, works offline).
/// The page template lives in <c>Resources/Web/diagram.html</c> and is embedded into the assembly.
/// </summary>
public static class DiagramHtmlBuilder
{
    public const string TemplateResourceName = "AIDatabaseAnalyzer.Web.diagram.html";

    /// <param name="theme">"auto", "light" or "dark".</param>
    /// <param name="embedded">True inside the app (the host provides the export buttons); false for a standalone file.</param>
    public static string Build(DatabaseSchema schema, string theme = "auto", bool embedded = true, string? template = null,
        bool includeSamples = true, DiagramContext? context = null)
    {
        template ??= LoadTemplate();

        return template
            .Replace("__THEME__", theme is "light" or "dark" ? theme : "auto")
            .Replace("/*__VARS__*/", HtmlTheme.Variables)
            .Replace("__AURORA__", HtmlTheme.AuroraBackground)
            .Replace("__FONT__", HtmlTheme.FontStack)
            .Replace("__MONO__", HtmlTheme.MonoStack)
            .Replace("__EMBEDDED__", embedded ? "true" : "false")
            .Replace("__DATA__", BuildDataJson(schema, includeSamples, context)); // last: the data must never be touched by token replacement
    }

    private static string LoadTemplate()
    {
        var assembly = typeof(DiagramHtmlBuilder).Assembly;
        using var stream = assembly.GetManifestResourceStream(TemplateResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{TemplateResourceName}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>Opening and closing identifier quotes, and the identifiers that can stay unquoted, per engine.</summary>
    private static (string Open, string Close, string Safe) Quoting(DbEngine engine) => engine switch
    {
        DbEngine.SqlServer => ("[", "]", SqlCatalog.SafeIdentifierPattern(engine)),
        DbEngine.MySql => ("`", "`", SqlCatalog.SafeIdentifierPattern(engine)),
        _ => ("\"", "\"", SqlCatalog.SafeIdentifierPattern(engine))
    };

    public static string BuildDataJson(DatabaseSchema schema, bool includeSamples = true, DiagramContext? context = null)
    {
        var tableNames = new HashSet<string>(schema.Tables.Select(t => t.FullName), StringComparer.OrdinalIgnoreCase);

        // findings per table, most severe first
        var rules = (context?.Insights?.Findings ?? Array.Empty<Finding>()).Where(f => f.Tables.Count > 0).OrderByDescending(f => f.Severity).ToList();
        var findingsByTable = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < rules.Count; i++)
            foreach (string table in rules[i].Tables)
            {
                if (!findingsByTable.TryGetValue(table, out var list)) findingsByTable[table] = list = new List<int>();
                list.Add(i);
            }

        var report = context?.Report;
        var mentions = report is null ? new Dictionary<string, List<int>>() : SchemaLinks.Mentions(report, schema);
        var quoting = Quoting(schema.Engine);
        var fkColumns = schema.ForeignKeys
            .GroupBy(f => f.FromTable, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.SelectMany(f => f.FromColumns).ToHashSet(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();

            w.WriteStartObject("meta");
            w.WriteString("db", schema.DatabaseName);
            w.WriteString("engine", schema.Engine.DisplayName());
            w.WriteStartArray("q");
            w.WriteStringValue(quoting.Open);
            w.WriteStringValue(quoting.Close);
            w.WriteStringValue(quoting.Safe);
            w.WriteEndArray();
            w.WriteStartArray("rw");
            foreach (string word in SqlCatalog.ReservedWords) w.WriteStringValue(word.ToLowerInvariant());
            w.WriteEndArray();
            if (context?.Insights is { } insights)
            {
                w.WriteNumber("score", insights.Score);
                w.WriteString("grade", insights.Grade);
            }
            w.WriteEndObject();

            w.WriteStartArray("rules");
            foreach (var f in rules)
            {
                w.WriteStartObject();
                w.WriteString("id", f.RuleId);
                w.WriteNumber("s", (int)f.Severity);
                w.WriteString("t", f.Title);
                w.WriteString("r", f.Recommendation);
                w.WriteNumber("n", f.Tables.Count);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartObject("report");
            if (report is not null)
            {
                w.WriteString("id", report.Id);
                w.WriteStartArray("s");
                foreach (var section in report.Sections) w.WriteStringValue(section.Title);
                w.WriteEndArray();
            }
            w.WriteEndObject();

            w.WriteStartObject("init");
            if (context?.FocusTable is { Length: > 0 } focusTable) w.WriteString("table", focusTable);
            if (context?.FocusRule is { Length: > 0 } focusRule) w.WriteString("rule", focusRule);
            if (context?.HealthLens == true || context?.FocusRule is { Length: > 0 }) w.WriteBoolean("health", true);
            w.WriteEndObject();

            w.WriteStartArray("tables");
            foreach (var table in schema.Tables)
            {
                fkColumns.TryGetValue(table.FullName, out var fks);

                w.WriteStartObject();
                w.WriteString("n", table.FullName);
                w.WriteString("q", SqlCatalog.SqlName(schema.Engine, table));
                if (table.RowCount is { } rows) w.WriteNumber("r", rows);
                if (findingsByTable.TryGetValue(table.FullName, out var tableFindings))
                {
                    w.WriteStartArray("f");
                    foreach (int i in tableFindings) w.WriteNumberValue(i);
                    w.WriteEndArray();
                }
                if (mentions.TryGetValue(table.FullName, out var sections))
                {
                    w.WriteStartArray("m");
                    foreach (int i in sections) w.WriteNumberValue(i);
                    w.WriteEndArray();
                }

                w.WriteStartArray("c");
                foreach (var c in table.Columns)
                {
                    w.WriteStartObject();
                    w.WriteString("n", c.Name);
                    w.WriteString("t", c.DataType);
                    if (c.IsPrimaryKey) w.WriteNumber("pk", 1);
                    if (fks?.Contains(c.Name) == true) w.WriteNumber("fk", 1);
                    if (!c.IsNullable) w.WriteNumber("nn", 1);
                    if (c.IsIdentity) w.WriteNumber("id", 1);
                    w.WriteEndObject();
                }
                w.WriteEndArray();

                w.WriteStartArray("i");
                foreach (var ix in schema.Indexes.Where(i => string.Equals(i.Table, table.FullName, StringComparison.OrdinalIgnoreCase)))
                {
                    w.WriteStartObject();
                    w.WriteString("n", ix.Name);
                    if (ix.IsUnique) w.WriteNumber("u", 1);
                    if (ix.IsPrimary) w.WriteNumber("p", 1);
                    w.WriteStartArray("c");
                    foreach (var col in ix.Columns) w.WriteStringValue(col);
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                w.WriteEndArray();

                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("rels");
            foreach (var fk in schema.ForeignKeys.Where(f => tableNames.Contains(f.FromTable) && tableNames.Contains(f.ToTable)))
            {
                w.WriteStartObject();
                w.WriteString("a", fk.FromTable);
                w.WriteString("b", fk.ToTable);
                w.WriteStartArray("fc");
                foreach (var c in fk.FromColumns) w.WriteStringValue(c);
                w.WriteEndArray();
                w.WriteStartArray("tc");
                foreach (var c in fk.ToColumns) w.WriteStringValue(c);
                w.WriteEndArray();
                if (!string.IsNullOrEmpty(fk.Name)) w.WriteString("n", fk.Name);
                if (!string.IsNullOrEmpty(fk.OnDelete)) w.WriteString("d", fk.OnDelete);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("views");
            foreach (var v in schema.Views) w.WriteStringValue(v.Name);
            w.WriteEndArray();

            w.WriteStartObject("samples");
            foreach (var (table, rows) in includeSamples ? schema.SampleData : new Dictionary<string, List<Dictionary<string, object?>>>())
            {
                w.WriteStartArray(table);
                foreach (var row in rows.Take(3))
                {
                    w.WriteStartObject();
                    foreach (var (column, value) in row)
                    {
                        w.WritePropertyName(column);
                        switch (value)
                        {
                            case null: w.WriteNullValue(); break;
                            case bool b: w.WriteBooleanValue(b); break;
                            case long l: w.WriteNumberValue(l); break;
                            case int i: w.WriteNumberValue(i); break;
                            case double d when double.IsFinite(d): w.WriteNumberValue(d); break;
                            case decimal m: w.WriteNumberValue(m); break;
                            default: w.WriteStringValue(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)); break;
                        }
                    }
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }
            w.WriteEndObject();

            w.WriteEndObject();
        }

        // The JSON lives inside a <script> element: make sure it can never close it or open an HTML comment.
        return Encoding.UTF8.GetString(stream.ToArray()).Replace("<", "\\u003C");
    }
}
