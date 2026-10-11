namespace AIDatabaseAnalyzer.Models;

/// <summary>Everything the analyzer knows about one database. Serializable by <c>SchemaSerializer</c>.</summary>
public sealed class DatabaseSchema
{
    public string DatabaseName { get; set; } = "";
    public DbEngine Engine { get; set; }
    public string? ServerVersion { get; set; }
    public DateTime ExtractedAtUtc { get; set; } = DateTime.UtcNow;

    public List<TableInfo> Tables { get; } = new();
    public List<ForeignKeyInfo> ForeignKeys { get; } = new();
    public List<IndexInfo> Indexes { get; } = new();
    public List<TriggerInfo> Triggers { get; } = new();
    public List<ViewInfo> Views { get; } = new();
    public List<RoutineInfo> Routines { get; } = new();
    public List<ConstraintInfo> Constraints { get; } = new();

    /// <summary>Sample rows keyed by <see cref="TableInfo.FullName"/>.</summary>
    public Dictionary<string, List<Dictionary<string, object?>>> SampleData { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Non-fatal problems met while reading metadata (missing permissions, unsupported features...).</summary>
    public List<string> Warnings { get; } = new();

    /// <summary>False when the user excluded indexes; index based checks are then skipped.</summary>
    public bool IndexesIncluded { get; set; } = true;

    /// <summary>Which optional sections were read; snapshots only compare what both sides contain.</summary>
    public bool ViewsIncluded { get; set; } = true;
    public bool TriggersIncluded { get; set; } = true;
    public bool RoutinesIncluded { get; set; } = true;
    public bool ConstraintsIncluded { get; set; } = true;
    public bool DefinitionsIncluded { get; set; } = true;

    /// <summary>Only some of the tables were selected for extraction.</summary>
    public bool IsPartial { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public int ColumnCount => Tables.Sum(t => t.Columns.Count);

    public TableInfo? FindTable(string fullName) =>
        Tables.FirstOrDefault(t => string.Equals(t.FullName, fullName, StringComparison.OrdinalIgnoreCase));
}

public sealed class TableInfo
{
    public string? Schema { get; set; }
    public string Name { get; set; } = "";
    public long? RowCount { get; set; }
    public List<ColumnInfo> Columns { get; } = new();

    /// <summary>Set by the reader so that default schemas (dbo, public) are omitted from names.</summary>
    public string? DefaultSchema { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string FullName => Qualify(Schema, Name, DefaultSchema);

    /// <summary>Builds the display name of an object; the engine's default schema (dbo, public) is omitted.</summary>
    public static string Qualify(string? schema, string name, string? defaultSchema) =>
        string.IsNullOrEmpty(schema) || string.Equals(schema, defaultSchema, StringComparison.OrdinalIgnoreCase)
            ? name
            : schema + "." + name;

    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<ColumnInfo> PrimaryKeyColumns => Columns.Where(c => c.IsPrimaryKey);

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasPrimaryKey => Columns.Any(c => c.IsPrimaryKey);
}

public sealed class ColumnInfo
{
    public string Name { get; set; } = "";

    /// <summary>Type including its size, e.g. <c>nvarchar(255)</c>, <c>decimal(18,2)</c>, <c>varchar(max)</c>.</summary>
    public string DataType { get; set; } = "";

    /// <summary>Base type name without size information, e.g. <c>nvarchar</c>.</summary>
    public string BaseType { get; set; } = "";

    /// <summary>Maximum length in characters/bytes where meaningful; -1 means unbounded (MAX/TEXT/BLOB).</summary>
    public long? MaxLength { get; set; }

    public bool IsNullable { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsIdentity { get; set; }
    public string? DefaultValue { get; set; }
}

public sealed class ForeignKeyInfo
{
    public string Name { get; set; } = "";
    public string FromTable { get; set; } = "";
    public List<string> FromColumns { get; } = new();
    public string ToTable { get; set; } = "";
    public List<string> ToColumns { get; } = new();
    public string? OnDelete { get; set; }
    public string? OnUpdate { get; set; }
}

public sealed class IndexInfo
{
    public string Table { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Type { get; set; }
    public bool IsUnique { get; set; }
    public bool IsPrimary { get; set; }
    public List<string> Columns { get; } = new();
    public List<string> IncludedColumns { get; } = new();
    public string? Filter { get; set; }
}

public sealed class TriggerInfo
{
    public string Name { get; set; } = "";
    public string Table { get; set; } = "";
    public string? Events { get; set; }
    public string? Timing { get; set; }
    public bool IsDisabled { get; set; }
    public string? Definition { get; set; }
}

public sealed class ViewInfo
{
    public string Name { get; set; } = "";
    public string? Definition { get; set; }
}

public sealed class RoutineInfo
{
    public string Name { get; set; } = "";

    /// <summary>PROCEDURE or FUNCTION.</summary>
    public string Kind { get; set; } = "PROCEDURE";
    public List<string> Parameters { get; } = new();
    public string? Definition { get; set; }
}

public sealed class ConstraintInfo
{
    public string Table { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>CHECK or UNIQUE.</summary>
    public string Type { get; set; } = "";
    public string? Definition { get; set; }
}
