using System.Text.Json;

namespace AIDatabaseAnalyzer.Services;

public static class SchemaChunker
{
    public const int DefaultMaxCharacters = 20_000;

    public static IReadOnlyList<string> Split(string json, int maxCharacters = DefaultMaxCharacters)
    {
        if (maxCharacters < 256) throw new ArgumentOutOfRangeException(nameof(maxCharacters));
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("Schema must be a JSON object.");

        var shared = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var tables = new List<KeyValuePair<string, JsonElement>>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.NameEquals("Tables") && property.Value.ValueKind == JsonValueKind.Object)
                tables.AddRange(property.Value.EnumerateObject().Select(table =>
                    new KeyValuePair<string, JsonElement>(table.Name, table.Value.Clone())));
            else
                shared[property.Name] = property.Value.Clone();
        }

        string Serialize(Dictionary<string, JsonElement> selected) =>
            JsonSerializer.Serialize(new Dictionary<string, object>(shared.Select(p =>
                new KeyValuePair<string, object>(p.Key, p.Value)), StringComparer.Ordinal)
            {
                ["Tables"] = selected
            });

        var empty = Serialize(new Dictionary<string, JsonElement>(StringComparer.Ordinal));
        if (empty.Length > maxCharacters)
            throw new InvalidOperationException("Non-table metadata exceeds the model request budget. Exclude optional components or narrow the database scope.");

        if (tables.Count == 0) return new[] { empty };
        var chunks = new List<string>();
        var current = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            current.Add(table.Key, table.Value);
            var candidate = Serialize(current);
            if (candidate.Length <= maxCharacters) continue;
            current.Remove(table.Key);
            if (current.Count == 0)
                throw new InvalidOperationException($"Table {table.Key} exceeds the model request budget. Select a smaller scope.");
            chunks.Add(Serialize(current));
            current = new Dictionary<string, JsonElement>(StringComparer.Ordinal) { [table.Key] = table.Value };
            if (Serialize(current).Length > maxCharacters)
                throw new InvalidOperationException($"Table {table.Key} exceeds the model request budget. Select a smaller scope.");
        }
        if (current.Count > 0) chunks.Add(Serialize(current));
        return chunks;
    }
}
