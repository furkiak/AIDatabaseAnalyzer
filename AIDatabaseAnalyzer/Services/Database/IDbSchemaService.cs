using AIDatabaseAnalyzer.Models;

namespace AIDatabaseAnalyzer.Services.Database;

/// <summary>What to read from the database and how to treat sample values.</summary>
public sealed class SchemaExtractionOptions
{
    /// <summary>Full names of the tables to include. <c>null</c> means every table.</summary>
    public ISet<string>? SelectedTables { get; set; }

    public bool IncludeViews { get; set; } = true;
    public bool IncludeIndexes { get; set; } = true;
    public bool IncludeTriggers { get; set; } = true;
    public bool IncludeRoutines { get; set; } = true;
    public bool IncludeConstraints { get; set; } = true;

    /// <summary>Include the SQL source of views, triggers and routines (needed for injection / complexity audits).</summary>
    public bool IncludeDefinitions { get; set; } = true;

    public bool IncludeSampleData { get; set; }

    /// <summary>Mask emails, phone numbers, card numbers and other sensitive looking values in sample rows.</summary>
    public bool MaskSampleData { get; set; } = true;

    public int SampleRowCount { get; set; } = 3;

    /// <summary>Longest SQL definition kept per object (characters).</summary>
    public int MaxDefinitionChars { get; set; } = 3000;

    /// <summary>Tables up to this size are sampled randomly; larger ones return their first rows to avoid full scans.</summary>
    public long RandomSampleMaxRows { get; set; } = 50_000;
}

public interface IDbSchemaService
{
    Task<IReadOnlyList<string>> GetDatabasesAsync(ConnectionSettings connection, CancellationToken ct = default);

    Task<IReadOnlyList<TableInfo>> GetTablesAsync(ConnectionSettings connection, string database, CancellationToken ct = default);

    Task<DatabaseSchema> ExtractSchemaAsync(
        ConnectionSettings connection,
        string database,
        SchemaExtractionOptions options,
        IProgress<string>? progress = null,
        CancellationToken ct = default);
}
