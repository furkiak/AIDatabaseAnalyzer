using System.Security.Cryptography;
using System.Text;
using AIDatabaseAnalyzer.Models;
using AIDatabaseAnalyzer.Services.Database;

namespace AIDatabaseAnalyzer.Services.Insights;

/// <summary>A column whose definition changed between two snapshots.</summary>
public sealed record ColumnChange(string Column, string Before, string After, bool Breaking);

public sealed class TableDiff
{
    public required string Table { get; init; }
    public List<string> AddedColumns { get; } = new();
    public List<string> RemovedColumns { get; } = new();
    public List<ColumnChange> ChangedColumns { get; } = new();
    public bool IsEmpty => AddedColumns.Count == 0 && RemovedColumns.Count == 0 && ChangedColumns.Count == 0;
}

/// <summary>Structural differences between two versions of a schema ("drift").</summary>
public sealed class SchemaDiff
{
    public List<string> AddedTables { get; } = new();
    public List<string> RemovedTables { get; } = new();
    public List<TableDiff> ChangedTables { get; } = new();
    public List<string> AddedForeignKeys { get; } = new();
    public List<string> RemovedForeignKeys { get; } = new();
    public List<string> AddedIndexes { get; } = new();
    public List<string> RemovedIndexes { get; } = new();
    public List<string> AddedViews { get; } = new();
    public List<string> RemovedViews { get; } = new();
    public List<string> ChangedViews { get; } = new();
    public List<string> AddedRoutines { get; } = new();
    public List<string> RemovedRoutines { get; } = new();
    public List<string> ChangedRoutines { get; } = new();
    public List<string> AddedTriggers { get; } = new();
    public List<string> RemovedTriggers { get; } = new();
    public List<string> AddedConstraints { get; } = new();
    public List<string> RemovedConstraints { get; } = new();

    /// <summary>Tables whose estimated row count changed noticeably (not counted as a structural change).</summary>
    public List<(string Table, long? Before, long? After)> RowGrowth { get; } = new();

    /// <summary>Sections that could not be compared because one side did not extract them.</summary>
    public List<string> NotCompared { get; } = new();

    public int ColumnChanges => ChangedTables.Sum(t => t.AddedColumns.Count + t.RemovedColumns.Count + t.ChangedColumns.Count);

    /// <summary>Changes that can break applications or lose data: dropped tables or columns, narrowed or retyped columns, new NOT NULLs.</summary>
    public int BreakingChanges =>
        RemovedTables.Count + ChangedTables.Sum(t => t.RemovedColumns.Count + t.ChangedColumns.Count(c => c.Breaking));

    public bool IsEmpty =>
        AddedTables.Count + RemovedTables.Count + ChangedTables.Count + AddedForeignKeys.Count + RemovedForeignKeys.Count
        + AddedIndexes.Count + RemovedIndexes.Count + AddedViews.Count + RemovedViews.Count + ChangedViews.Count
        + AddedRoutines.Count + RemovedRoutines.Count + ChangedRoutines.Count + AddedTriggers.Count + RemovedTriggers.Count
        + AddedConstraints.Count + RemovedConstraints.Count == 0;

    /// <summary>One line for banners: "+2 tables, 5 column changes (1 breaking), +3 indexes".</summary>
    public string Summary
    {
        get
        {
            if (IsEmpty) return "No structural changes.";
            var parts = new List<string>();
            void Add(int added, int removed, string noun)
            {
                if (added > 0) parts.Add($"+{added} {noun}{(added == 1 ? "" : "s")}");
                if (removed > 0) parts.Add($"−{removed} {noun}{(removed == 1 ? "" : "s")}");
            }
            Add(AddedTables.Count, RemovedTables.Count, "table");
            if (ColumnChanges > 0) parts.Add($"{ColumnChanges} column change{(ColumnChanges == 1 ? "" : "s")}");
            Add(AddedForeignKeys.Count, RemovedForeignKeys.Count, "foreign key");
            Add(AddedIndexes.Count, RemovedIndexes.Count, "index");
            Add(AddedViews.Count, RemovedViews.Count, "view");
            if (ChangedViews.Count > 0) parts.Add($"{ChangedViews.Count} view(s) redefined");
            Add(AddedRoutines.Count, RemovedRoutines.Count, "routine");
            if (ChangedRoutines.Count > 0) parts.Add($"{ChangedRoutines.Count} routine(s) changed");
            Add(AddedTriggers.Count, RemovedTriggers.Count, "trigger");
            Add(AddedConstraints.Count, RemovedConstraints.Count, "constraint");
            string text = string.Join(", ", parts);
            return BreakingChanges > 0 ? $"{text} — {BreakingChanges} potentially breaking" : text;
        }
    }

    // ------------------------------------------------------------------ comparison

    public static SchemaDiff Compare(DatabaseSchema before, DatabaseSchema after)
    {
        var diff = new SchemaDiff();
        var comparer = StringComparer.OrdinalIgnoreCase;
        var oldTables = before.Tables.ToDictionary(t => t.FullName, comparer);
        var newTables = after.Tables.ToDictionary(t => t.FullName, comparer);

        foreach (var (name, table) in newTables.OrderBy(t => t.Key, comparer))
        {
            if (!oldTables.TryGetValue(name, out var old))
            {
                diff.AddedTables.Add(name);
                continue;
            }

            var tableDiff = CompareColumns(old, table);
            if (!tableDiff.IsEmpty) diff.ChangedTables.Add(tableDiff);

            if (old.RowCount is { } a && table.RowCount is { } b && a != b && Math.Abs(b - a) >= Math.Max(100, a / 10))
                diff.RowGrowth.Add((name, a, b));
        }

        foreach (var name in oldTables.Keys.Where(k => !newTables.ContainsKey(k)).OrderBy(k => k, comparer))
            diff.RemovedTables.Add(name);

        // Tables that exist on one side only bring their keys and indexes with them: report those with the table, not twice.
        bool BothHave(string table) => oldTables.ContainsKey(table) && newTables.ContainsKey(table);

        Changes(before.ForeignKeys.Where(f => BothHave(f.FromTable)).Select(SchemaSerializer.DescribeForeignKey),
                after.ForeignKeys.Where(f => BothHave(f.FromTable)).Select(SchemaSerializer.DescribeForeignKey),
                diff.AddedForeignKeys, diff.RemovedForeignKeys);

        if (before.IndexesIncluded && after.IndexesIncluded)
            Changes(before.Indexes.Where(i => BothHave(i.Table)).Select(SchemaSerializer.DescribeIndex),
                    after.Indexes.Where(i => BothHave(i.Table)).Select(SchemaSerializer.DescribeIndex),
                    diff.AddedIndexes, diff.RemovedIndexes);
        else diff.NotCompared.Add("indexes");

        bool definitions = before.DefinitionsIncluded && after.DefinitionsIncluded;
        if (before.ViewsIncluded && after.ViewsIncluded)
            Objects(before.Views.ToDictionary(v => v.Name, v => v.Definition, comparer), after.Views.ToDictionary(v => v.Name, v => v.Definition, comparer),
                    definitions, diff.AddedViews, diff.RemovedViews, diff.ChangedViews);
        else diff.NotCompared.Add("views");

        if (before.RoutinesIncluded && after.RoutinesIncluded)
            Objects(Unique(before.Routines.Select(r => (r.Name + "(" + string.Join(", ", r.Parameters) + ")", r.Definition))),
                    Unique(after.Routines.Select(r => (r.Name + "(" + string.Join(", ", r.Parameters) + ")", r.Definition))),
                    definitions, diff.AddedRoutines, diff.RemovedRoutines, diff.ChangedRoutines);
        else diff.NotCompared.Add("stored procedures and functions");

        if (before.TriggersIncluded && after.TriggersIncluded)
            Changes(before.Triggers.Where(t => BothHave(t.Table)).Select(t => $"{t.Table}.{t.Name}"),
                    after.Triggers.Where(t => BothHave(t.Table)).Select(t => $"{t.Table}.{t.Name}"),
                    diff.AddedTriggers, diff.RemovedTriggers);
        else diff.NotCompared.Add("triggers");

        if (before.ConstraintsIncluded && after.ConstraintsIncluded)
            Changes(before.Constraints.Where(c => BothHave(c.Table)).Select(c => $"{c.Table}: {c.Definition}"),
                    after.Constraints.Where(c => BothHave(c.Table)).Select(c => $"{c.Table}: {c.Definition}"),
                    diff.AddedConstraints, diff.RemovedConstraints);
        else diff.NotCompared.Add("CHECK constraints");

        if (before.IsPartial || after.IsPartial) diff.NotCompared.Add("tables outside the selected scope");
        return diff;
    }

    private static TableDiff CompareColumns(TableInfo old, TableInfo current)
    {
        var diff = new TableDiff { Table = current.FullName };
        var oldColumns = old.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var newColumns = current.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var column in current.Columns)
        {
            if (!oldColumns.TryGetValue(column.Name, out var was))
            {
                diff.AddedColumns.Add(SchemaSerializer.DescribeColumn(column));
                continue;
            }

            string before = SchemaSerializer.DescribeColumn(was), after = SchemaSerializer.DescribeColumn(column);
            if (!string.Equals(before, after, StringComparison.Ordinal))
                diff.ChangedColumns.Add(new ColumnChange(column.Name, before, after, IsBreaking(was, column)));
        }

        foreach (var column in old.Columns.Where(c => !newColumns.ContainsKey(c.Name)))
            diff.RemovedColumns.Add(SchemaSerializer.DescribeColumn(column));
        return diff;
    }

    /// <summary>NOT NULL added, type family changed, or a length / precision reduced.</summary>
    private static bool IsBreaking(ColumnInfo before, ColumnInfo after)
    {
        if (before.IsNullable && !after.IsNullable) return true;
        if (SchemaInsightsService.TypeFamily(before.BaseType) != SchemaInsightsService.TypeFamily(after.BaseType)) return true;
        if (before.MaxLength is > 0 and var oldLength && after.MaxLength is > 0 and var newLength && newLength < oldLength) return true;
        if (before.MaxLength == -1 && after.MaxLength is > 0) return true; // MAX/LOB → bounded
        return false;
    }

    private static Dictionary<string, string?> Unique(IEnumerable<(string Key, string? Value)> items)
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in items) map.TryAdd(key, value);
        return map;
    }

    private static void Changes(IEnumerable<string> before, IEnumerable<string> after, List<string> added, List<string> removed)
    {
        var old = new HashSet<string>(before, StringComparer.OrdinalIgnoreCase);
        var current = new HashSet<string>(after, StringComparer.OrdinalIgnoreCase);
        added.AddRange(current.Where(x => !old.Contains(x)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        removed.AddRange(old.Where(x => !current.Contains(x)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
    }

    private static void Objects(Dictionary<string, string?> before, Dictionary<string, string?> after, bool compareDefinitions,
        List<string> added, List<string> removed, List<string> changed)
    {
        added.AddRange(after.Keys.Where(k => !before.ContainsKey(k)).OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
        removed.AddRange(before.Keys.Where(k => !after.ContainsKey(k)).OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
        if (!compareDefinitions) return;
        foreach (var (name, definition) in after)
            if (before.TryGetValue(name, out var old) && !string.IsNullOrEmpty(old) && !string.IsNullOrEmpty(definition)
                && Normalize(old) != Normalize(definition))
                changed.Add(name);
    }

    private static string Normalize(string sql) => string.Join(" ", sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    // ------------------------------------------------------------------ fingerprint and Markdown

    /// <summary>A hash of the structure only (no row counts, no samples): equal fingerprints mean nothing changed.</summary>
    public static string Fingerprint(DatabaseSchema schema)
    {
        var sb = new StringBuilder();
        foreach (var t in schema.Tables.OrderBy(t => t.FullName, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append("T|").Append(t.FullName).Append('\n');
            foreach (var c in t.Columns) sb.Append("C|").Append(SchemaSerializer.DescribeColumn(c)).Append('\n');
        }
        foreach (string line in schema.ForeignKeys.Select(SchemaSerializer.DescribeForeignKey)
                     .Concat(schema.Indexes.Select(SchemaSerializer.DescribeIndex))
                     .Concat(schema.Views.Select(v => "V|" + v.Name + "|" + Normalize(v.Definition ?? "")))
                     .Concat(schema.Routines.Select(r => "R|" + r.Name + "|" + Normalize(r.Definition ?? "")))
                     .Concat(schema.Triggers.Select(t => "G|" + t.Table + "." + t.Name))
                     .Concat(schema.Constraints.Select(c => "K|" + c.Table + "|" + c.Definition))
                     .OrderBy(x => x, StringComparer.Ordinal))
            sb.Append(line).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16];
    }

    public string ToMarkdown(string beforeLabel, string afterLabel)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"**{beforeLabel}** → **{afterLabel}**");
        sb.AppendLine();
        sb.AppendLine(IsEmpty ? "No structural changes." : Summary + ".");
        sb.AppendLine();

        void List(string title, IReadOnlyCollection<string> items, string marker)
        {
            if (items.Count == 0) return;
            sb.AppendLine($"## {title} ({items.Count})");
            foreach (string item in items) sb.AppendLine($"- {marker} `{item}`");
            sb.AppendLine();
        }

        List("New tables", AddedTables, "➕");
        List("Dropped tables", RemovedTables, "⚠️ ➖");

        if (ChangedTables.Count > 0)
        {
            sb.AppendLine($"## Changed tables ({ChangedTables.Count})");
            foreach (var t in ChangedTables)
            {
                sb.AppendLine($"### `{t.Table}`");
                foreach (string c in t.AddedColumns) sb.AppendLine($"- ➕ `{c}`");
                foreach (string c in t.RemovedColumns) sb.AppendLine($"- ⚠️ ➖ `{c}`");
                foreach (var c in t.ChangedColumns)
                    sb.AppendLine($"- {(c.Breaking ? "⚠️ " : "")}✏️ `{c.Before}` → `{c.After}`");
                sb.AppendLine();
            }
        }

        List("New foreign keys", AddedForeignKeys, "➕");
        List("Dropped foreign keys", RemovedForeignKeys, "➖");
        List("New indexes", AddedIndexes, "➕");
        List("Dropped indexes", RemovedIndexes, "➖");
        List("New views", AddedViews, "➕");
        List("Dropped views", RemovedViews, "⚠️ ➖");
        List("Redefined views", ChangedViews, "✏️");
        List("New procedures / functions", AddedRoutines, "➕");
        List("Dropped procedures / functions", RemovedRoutines, "⚠️ ➖");
        List("Changed procedures / functions", ChangedRoutines, "✏️");
        List("New triggers", AddedTriggers, "➕");
        List("Dropped triggers", RemovedTriggers, "➖");
        List("New CHECK constraints", AddedConstraints, "➕");
        List("Dropped CHECK constraints", RemovedConstraints, "➖");

        if (RowGrowth.Count > 0)
        {
            sb.AppendLine("## Data growth");
            sb.AppendLine("| Table | Before | After | Change |");
            sb.AppendLine("|---|---:|---:|---:|");
            foreach (var (table, a, b) in RowGrowth.OrderByDescending(g => Math.Abs((g.After ?? 0) - (g.Before ?? 0))).Take(25))
            {
                long delta = (b ?? 0) - (a ?? 0);
                string pct = a is > 0 ? $" ({(double)delta / a.Value:+0%;-0%})" : "";
                sb.AppendLine($"| `{table}` | {a:N0} | {b:N0} | {delta:+#,0;-#,0}{pct} |");
            }
            sb.AppendLine();
        }

        if (NotCompared.Count > 0)
            sb.AppendLine($"*Not compared because one side did not extract them: {string.Join(", ", NotCompared)}.*");
        return sb.ToString();
    }
}
