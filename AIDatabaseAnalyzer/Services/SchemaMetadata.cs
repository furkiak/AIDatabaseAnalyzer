namespace AIDatabaseAnalyzer.Services;

public sealed record ColumnMetadata(string Column, string Type, int? MaxLen, bool IsNullable, bool IsPrimaryKey);

public sealed record ForeignKeyColumn(string From, string To, int Ordinal);

public sealed class ForeignKeyMetadata
{
    public required string Name { get; init; }
    public required DatabaseObjectIdentity FromIdentity { get; init; }
    public required DatabaseObjectIdentity ToIdentity { get; init; }
    public string From => FromIdentity.Key;
    public string To => ToIdentity.Key;
    public List<ForeignKeyColumn> Columns { get; } = new();
}

public sealed record IndexColumn(string? Name, int Ordinal, bool Included);

public sealed class IndexMetadata
{
    public required DatabaseObjectIdentity TableIdentity { get; init; }
    public string Table => TableIdentity.Key;
    public required string Name { get; init; }
    public required string Type { get; init; }
    public bool Unique { get; init; }
    public string? Filter { get; init; }
    public string? Definition { get; init; }
    public List<IndexColumn> Columns { get; } = new();
}

public sealed record ConstraintMetadata(DatabaseObjectIdentity TableIdentity, string Name, string Type, string? Definition)
{
    public string Table => TableIdentity.Key;
}

public sealed record TriggerMetadata(DatabaseObjectIdentity Identity, DatabaseObjectIdentity TableIdentity, bool? Disabled)
{
    public string Name => Identity.Name;
    public string Table => TableIdentity.Key;
}
