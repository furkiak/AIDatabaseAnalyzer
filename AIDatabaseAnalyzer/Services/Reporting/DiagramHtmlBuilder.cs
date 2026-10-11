using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AIDatabaseAnalyzer.Models;

namespace AIDatabaseAnalyzer.Services.Reporting;

/// <summary>
/// Builds the self-contained interactive ER diagram page (no external scripts, works offline).
/// The page template lives in <c>Resources/Web/diagram.html</c> and is embedded into the assembly.
/// </summary>
public static class DiagramHtmlBuilder
{
    public const string TemplateResourceName = "AIDatabaseAnalyzer.Web.diagram.html";

    /// <param name="theme">"auto", "light" or "dark".</param>
    /// <param name="embedded">True inside the app (the host provides the export buttons); false for a standalone file.</param>
    public static string Build(DatabaseSchema schema, string theme = "auto", bool embedded = true, string? template = null, bool includeSamples = true)
    {
        template ??= LoadTemplate();

        return template
            .Replace("__THEME__", theme is "light" or "dark" ? theme : "auto")
            .Replace("/*__VARS__*/", HtmlTheme.Variables)
            .Replace("__AURORA__", HtmlTheme.AuroraBackground)
            .Replace("__FONT__", HtmlTheme.FontStack)
            .Replace("__MONO__", HtmlTheme.MonoStack)
            .Replace("__EMBEDDED__", embedded ? "true" : "false")
            .Replace("__DATA__", BuildDataJson(schema, includeSamples)); // last: the data must never be touched by token replacement
    }

    private static string LoadTemplate()
    {
        var assembly = typeof(DiagramHtmlBuilder).Assembly;
        using var stream = assembly.GetManifestResourceStream(TemplateResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{TemplateResourceName}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public static string BuildDataJson(DatabaseSchema schema, bool includeSamples = true)
    {
        var tableNames = new HashSet<string>(schema.Tables.Select(t => t.FullName), StringComparer.OrdinalIgnoreCase);
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
            w.WriteEndObject();

            w.WriteStartArray("tables");
            foreach (var table in schema.Tables)
            {
                fkColumns.TryGetValue(table.FullName, out var fks);

                w.WriteStartObject();
                w.WriteString("n", table.FullName);
                if (table.RowCount is { } rows) w.WriteNumber("r", rows);

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
