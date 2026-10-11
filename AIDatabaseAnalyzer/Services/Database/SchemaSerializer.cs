using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AIDatabaseAnalyzer.Models;

namespace AIDatabaseAnalyzer.Services.Database;

/// <summary>
/// Converts a <see cref="DatabaseSchema"/> into the compact JSON that is sent to AI models, and into a
/// human readable Markdown data dictionary. Columns are written as one short string each, which needs
/// roughly half the tokens of the usual object-per-column layout.
/// </summary>
public static class SchemaSerializer
{
    public static string ToJson(DatabaseSchema schema, bool indented)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = indented,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }))
        {
            Write(w, schema);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Rough token count (about 3.5 characters per token for JSON / SQL heavy text).</summary>
    public static int EstimateTokens(string text) => text.Length == 0 ? 0 : (int)Math.Ceiling(text.Length / 3.5);

    // ------------------------------------------------------------------ JSON

    private static void Write(Utf8JsonWriter w, DatabaseSchema schema)
    {
        w.WriteStartObject();
        w.WriteString("Database", schema.DatabaseName);
        w.WriteString("Engine", schema.Engine.DisplayName());
        if (!string.IsNullOrWhiteSpace(schema.ServerVersion)) w.WriteString("Version", schema.ServerVersion);

        w.WriteStartObject("Tables");
        foreach (var table in schema.Tables)
        {
            w.WriteStartObject(table.FullName);
            if (table.RowCount is { } rows) w.WriteNumber("Rows", rows);
            w.WriteStartArray("Columns");
            foreach (var column in table.Columns) w.WriteStringValue(DescribeColumn(column));
            w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndObject();

        if (schema.ForeignKeys.Count > 0)
        {
            w.WriteStartArray("ForeignKeys");
            foreach (var fk in schema.ForeignKeys) w.WriteStringValue(DescribeForeignKey(fk));
            w.WriteEndArray();
        }

        if (schema.Indexes.Count > 0)
        {
            w.WriteStartArray("Indexes");
            foreach (var ix in schema.Indexes) w.WriteStringValue(DescribeIndex(ix));
            w.WriteEndArray();
        }

        if (schema.Constraints.Count > 0)
        {
            w.WriteStartArray("Constraints");
            foreach (var c in schema.Constraints)
                w.WriteStringValue($"{c.Table}.{c.Name} {c.Type} {c.Definition}".TrimEnd());
            w.WriteEndArray();
        }

        if (schema.Triggers.Count > 0)
        {
            w.WriteStartArray("Triggers");
            foreach (var t in schema.Triggers)
            {
                w.WriteStartObject();
                w.WriteString("Name", t.Name);
                w.WriteString("Table", t.Table);
                if (!string.IsNullOrEmpty(t.Timing) || !string.IsNullOrEmpty(t.Events))
                    w.WriteString("When", $"{t.Timing} {t.Events}".Trim());
                if (t.IsDisabled) w.WriteBoolean("Disabled", true);
                if (!string.IsNullOrWhiteSpace(t.Definition)) w.WriteString("Definition", t.Definition);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }

        if (schema.Views.Count > 0)
        {
            w.WriteStartArray("Views");
            foreach (var v in schema.Views)
            {
                if (string.IsNullOrWhiteSpace(v.Definition))
                {
                    w.WriteStringValue(v.Name);
                    continue;
                }
                w.WriteStartObject();
                w.WriteString("Name", v.Name);
                w.WriteString("Definition", v.Definition);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }

        if (schema.Routines.Count > 0)
        {
            w.WriteStartArray("StoredProcedures");
            foreach (var r in schema.Routines)
            {
                w.WriteStartObject();
                w.WriteString("Name", r.Name);
                w.WriteString("Kind", r.Kind);
                if (r.Parameters.Count > 0) w.WriteString("Parameters", string.Join(", ", r.Parameters));
                if (!string.IsNullOrWhiteSpace(r.Definition)) w.WriteString("Definition", r.Definition);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }

        if (schema.SampleData.Count > 0)
        {
            w.WriteStartObject("SampleData");
            foreach (var (table, rows) in schema.SampleData)
            {
                w.WriteStartArray(table);
                foreach (var row in rows)
                {
                    w.WriteStartObject();
                    foreach (var (column, value) in row)
                    {
                        w.WritePropertyName(column);
                        WriteValue(w, value);
                    }
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }
            w.WriteEndObject();
        }

        w.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter w, object? value)
    {
        switch (value)
        {
            case null: w.WriteNullValue(); break;
            case bool b: w.WriteBooleanValue(b); break;
            case long l: w.WriteNumberValue(l); break;
            case int i: w.WriteNumberValue(i); break;
            case short s: w.WriteNumberValue(s); break;
            case byte by: w.WriteNumberValue(by); break;
            case sbyte sb: w.WriteNumberValue(sb); break;
            case ushort us: w.WriteNumberValue(us); break;
            case uint ui: w.WriteNumberValue(ui); break;
            case double d: w.WriteNumberValue(d); break;
            case float f: w.WriteNumberValue(f); break;
            case decimal m: w.WriteNumberValue(m); break;
            default: w.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture)); break;
        }
    }

    // ------------------------------------------------------------------ descriptions

    public static string DescribeColumn(ColumnInfo c)
    {
        var sb = new StringBuilder(c.Name).Append(' ').Append(c.DataType);
        if (c.IsPrimaryKey) sb.Append(" PK");
        if (c.IsIdentity) sb.Append(" IDENTITY");
        sb.Append(c.IsNullable ? " NULL" : " NOT NULL");
        if (!string.IsNullOrEmpty(c.DefaultValue)) sb.Append(" DEFAULT ").Append(c.DefaultValue);
        return sb.ToString();
    }

    public static string DescribeForeignKey(ForeignKeyInfo fk)
    {
        var sb = new StringBuilder()
            .Append(fk.FromTable).Append('(').Append(string.Join(", ", fk.FromColumns)).Append(") -> ")
            .Append(fk.ToTable).Append('(').Append(string.Join(", ", fk.ToColumns)).Append(')');
        if (!string.IsNullOrEmpty(fk.Name)) sb.Append(" [").Append(fk.Name).Append(']');
        if (!string.IsNullOrEmpty(fk.OnDelete)) sb.Append(" ON DELETE ").Append(fk.OnDelete);
        if (!string.IsNullOrEmpty(fk.OnUpdate)) sb.Append(" ON UPDATE ").Append(fk.OnUpdate);
        return sb.ToString();
    }

    public static string DescribeIndex(IndexInfo ix)
    {
        var sb = new StringBuilder(ix.Table).Append('.').Append(ix.Name);
        if (ix.IsPrimary) sb.Append(" PRIMARY");
        else if (ix.IsUnique) sb.Append(" UNIQUE");
        if (!string.IsNullOrEmpty(ix.Type)) sb.Append(' ').Append(ix.Type);
        sb.Append(" (").Append(string.Join(", ", ix.Columns)).Append(')');
        if (ix.IncludedColumns.Count > 0) sb.Append(" INCLUDE (").Append(string.Join(", ", ix.IncludedColumns)).Append(')');
        if (!string.IsNullOrEmpty(ix.Filter)) sb.Append(" WHERE ").Append(ix.Filter);
        return sb.ToString();
    }

    // ------------------------------------------------------------------ Markdown data dictionary

    public static string ToMarkdownDictionary(DatabaseSchema schema)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Data dictionary — {schema.DatabaseName}");
        sb.AppendLine();
        sb.AppendLine($"*Engine:* {schema.Engine.DisplayName()}" + (string.IsNullOrWhiteSpace(schema.ServerVersion) ? "" : $" ({schema.ServerVersion})"));
        sb.AppendLine($"*Extracted:* {schema.ExtractedAtUtc:yyyy-MM-dd HH:mm} UTC · {schema.Tables.Count} tables · {schema.ColumnCount} columns · {schema.ForeignKeys.Count} foreign keys");
        sb.AppendLine();

        foreach (var table in schema.Tables)
        {
            sb.AppendLine($"## {table.FullName}");
            if (table.RowCount is { } rows) sb.AppendLine($"*≈ {rows:N0} rows*");
            sb.AppendLine();
            sb.AppendLine("| Column | Type | Null | Key | Default |");
            sb.AppendLine("|---|---|---|---|---|");

            var fkColumns = schema.ForeignKeys
                .Where(f => string.Equals(f.FromTable, table.FullName, StringComparison.OrdinalIgnoreCase))
                .SelectMany(f => f.FromColumns.Select((col, i) => (col, target: $"{f.ToTable}.{f.ToColumns.ElementAtOrDefault(i)}")))
                .ToLookup(x => x.col, x => x.target, StringComparer.OrdinalIgnoreCase);

            foreach (var c in table.Columns)
            {
                var keys = new List<string>();
                if (c.IsPrimaryKey) keys.Add("PK");
                if (c.IsIdentity) keys.Add("identity");
                foreach (var target in fkColumns[c.Name]) keys.Add("FK → " + target);

                sb.AppendLine($"| {Escape(c.Name)} | {Escape(c.DataType)} | {(c.IsNullable ? "yes" : "no")} | {Escape(string.Join(", ", keys))} | {Escape(c.DefaultValue ?? "")} |");
            }

            var indexes = schema.Indexes.Where(i => string.Equals(i.Table, table.FullName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (indexes.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("**Indexes**");
                sb.AppendLine();
                foreach (var ix in indexes) sb.AppendLine($"- `{ix.Name}`{(ix.IsPrimary ? " (primary)" : ix.IsUnique ? " (unique)" : "")}: {string.Join(", ", ix.Columns)}");
            }

            sb.AppendLine();
        }

        if (schema.Views.Count > 0)
        {
            sb.AppendLine("## Views");
            sb.AppendLine();
            foreach (var v in schema.Views) sb.AppendLine($"- `{v.Name}`");
            sb.AppendLine();
        }

        if (schema.Routines.Count > 0)
        {
            sb.AppendLine("## Stored procedures & functions");
            sb.AppendLine();
            foreach (var r in schema.Routines)
                sb.AppendLine($"- `{r.Name}` ({r.Kind.ToLowerInvariant()}){(r.Parameters.Count > 0 ? ": " + string.Join(", ", r.Parameters) : "")}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string Escape(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}
