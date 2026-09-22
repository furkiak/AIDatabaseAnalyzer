namespace AIDatabaseAnalyzer.Services;

public sealed record ColumnMetadata(string Column, string Type, int? MaxLen, bool IsNullable, bool IsPrimaryKey);

public sealed record ForeignKeyColumn(string From, string To, int Ordinal);

public sealed class ForeignKeyMetadata
{
    public required string Name { get; init; }
    public required string From { get; init; }
    public required string To { get; init; }
    public List<ForeignKeyColumn> Columns { get; } = new();
}

public sealed record IndexColumn(string? Name, int Ordinal, bool Included);

public sealed class IndexMetadata
{
    public required string Table { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public bool Unique { get; init; }
    public string? Filter { get; init; }
    public string? Definition { get; init; }
    public List<IndexColumn> Columns { get; } = new();
}

public sealed record ConstraintMetadata(string Table, string Name, string Type, string? Definition);
