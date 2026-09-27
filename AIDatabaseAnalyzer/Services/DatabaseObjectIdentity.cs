using System.Text;
using System.Text.Json.Serialization;

namespace AIDatabaseAnalyzer.Services;

public sealed record DatabaseObjectIdentity
{
    public string Schema { get; }
    public string Name { get; }

    public DatabaseObjectIdentity(string schema, string name)
    {
        if (string.IsNullOrEmpty(schema) || string.IsNullOrEmpty(name))
            throw new ArgumentException("A database object needs separate nonempty schema and name fields.");
        Schema = schema;
        Name = name;
    }

    [JsonIgnore]
    public string Key => NeedsQuotes(Schema) || NeedsQuotes(Name)
        ? $"[{Escape(Schema)}].[{Escape(Name)}]" : $"{Schema}.{Name}";

    private static bool NeedsQuotes(string value) => value.IndexOfAny(['.', '[', ']']) >= 0;
    private static string Escape(string value) => value.Replace("]", "]]", StringComparison.Ordinal);

    // Compatibility adapter for old unambiguous selections; live UI passes the typed identity.
    public static DatabaseObjectIdentity ParseKey(string key)
    {
        if (string.IsNullOrEmpty(key)) throw new ArgumentException("Choose a schema-qualified object key.", nameof(key));
        if (key[0] != '[')
        {
            var separator = key.IndexOf('.');
            if (separator <= 0 || separator != key.LastIndexOf('.') || separator == key.Length - 1)
                throw new ArgumentException("An ambiguous legacy object key needs separate schema and name fields or a bracket-qualified key.", nameof(key));
            return new DatabaseObjectIdentity(key[..separator], key[(separator + 1)..]);
        }
        var offset = 0;
        var schema = ReadPart(key, ref offset);
        if (offset >= key.Length || key[offset++] != '.') throw new ArgumentException("Invalid bracket-qualified object key.", nameof(key));
        var name = ReadPart(key, ref offset);
        if (offset != key.Length) throw new ArgumentException("Invalid bracket-qualified object key.", nameof(key));
        var identity = new DatabaseObjectIdentity(schema, name);
        if (identity.Key != key)
            throw new ArgumentException("Use a canonical object key or separate schema/name fields; a quoted alias can collide with literal bracket names.", nameof(key));
        return identity;
    }

    private static string ReadPart(string key, ref int offset)
    {
        if (offset >= key.Length || key[offset++] != '[') throw new ArgumentException("Invalid bracket-qualified object key.", nameof(key));
        var part = new StringBuilder();
        while (offset < key.Length)
        {
            var current = key[offset++];
            if (current != ']') { part.Append(current); continue; }
            if (offset < key.Length && key[offset] == ']') { part.Append(']'); offset++; continue; }
            return part.ToString();
        }
        throw new ArgumentException("Unclosed bracket-qualified object key.", nameof(key));
    }
}
