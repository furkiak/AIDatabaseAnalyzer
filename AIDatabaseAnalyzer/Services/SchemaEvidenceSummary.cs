using System.Text;
using System.Text.Json;

namespace AIDatabaseAnalyzer.Services;

public static class SchemaEvidenceSummary
{
    public static string Create(string modelSchemaJson)
    {
        using var document = JsonDocument.Parse(modelSchemaJson);
        var root = document.RootElement;
        var summary = new StringBuilder("Read-only metadata fields available to this review:\n");
        foreach (var manifest in new[] { "TableIdentities", "ViewIdentities", "RoutineIdentities" })
            if (root.TryGetProperty(manifest, out var identities) && identities.ValueKind == JsonValueKind.Object)
                foreach (var identity in identities.EnumerateObject())
                    summary.AppendLine($"- {manifest}[{JsonSerializer.Serialize(identity.Name)}]: separate Schema and Name fields");
        if (root.TryGetProperty("Tables", out var tables) && tables.ValueKind == JsonValueKind.Object)
        {
            foreach (var table in tables.EnumerateObject())
            {
                var path = $"Tables[{JsonSerializer.Serialize(table.Name)}]";
                var count = table.Value.ValueKind == JsonValueKind.Array ? table.Value.GetArrayLength() : 0;
                summary.AppendLine($"- {path}: {count} column records (Column, Type, MaxLen, IsNullable, IsPrimaryKey)");
            }
        }
        AppendNamedList(root, summary, "ForeignKeys", "Name");
        AppendNamedList(root, summary, "Indexes", "Name");
        AppendNamedList(root, summary, "Triggers", "Name");
        AppendNamedList(root, summary, "Constraints", "Name");
        AppendNamedList(root, summary, "Views", null);
        if (root.TryGetProperty("StoredProcedures", out var procedures) && procedures.ValueKind == JsonValueKind.Object)
            foreach (var procedure in procedures.EnumerateObject())
                summary.AppendLine($"- StoredProcedures[{JsonSerializer.Serialize(procedure.Name)}]: parameter names only");
        summary.AppendLine("SampleData is excluded from this review payload. Omitted fields are not evidence of absence.");
        return summary.ToString();
    }

    private static void AppendNamedList(JsonElement root, StringBuilder summary, string property, string? nameProperty)
    {
        if (!root.TryGetProperty(property, out var values) || values.ValueKind != JsonValueKind.Array) return;
        var i = 0;
        foreach (var value in values.EnumerateArray())
        {
            var name = nameProperty is null
                ? value.ValueKind == JsonValueKind.String ? value.GetString() : null
                : value.ValueKind == JsonValueKind.Object && value.TryGetProperty(nameProperty, out var named) ? named.ToString() : null;
            summary.AppendLine($"- {property}[{i++}]: {name ?? "unnamed"}");
        }
    }
}
