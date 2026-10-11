using System.Text.RegularExpressions;
using AIDatabaseAnalyzer.Models;

namespace AIDatabaseAnalyzer.Services.Reporting;

/// <summary>
/// Connects text to the schema: finds the table behind a name written by the AI (`dbo.Orders`, `Orders.CustomerId`,
/// "[sales].[orders]") or by the rule engine ("orders(customer_id) → customers"), so reports and findings can link to the diagram.
/// </summary>
public static partial class SchemaLinks
{
    [GeneratedRegex(@"`([^`\n]{1,200})`")]
    private static partial Regex InlineCode();

    /// <summary>Resolves a written object name to the table's full name, or null when it is not a table (or one of its columns).</summary>
    public static string? ResolveTable(string token, DatabaseSchema schema)
    {
        string name = Clean(token);
        if (name.Length == 0) return null;

        // "orders(customer_id)" → "orders"
        int paren = name.IndexOf('(');
        if (paren > 0) name = name[..paren].TrimEnd();

        if (Find(name, schema) is { } table) return table;

        // "orders.customer_id" or "sales.orders.total" → drop the column part
        int dot = name.LastIndexOf('.');
        return dot > 0 ? Find(name[..dot], schema) : null;
    }

    /// <summary>All tables a Markdown text refers to in inline code.</summary>
    public static IReadOnlySet<string> MentionedTables(string markdown, DatabaseSchema schema)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in InlineCode().Matches(markdown))
            if (ResolveTable(m.Groups[1].Value, schema) is { } table) found.Add(table);
        return found;
    }

    /// <summary>Per table, the indexes of the report sections that mention it.</summary>
    public static Dictionary<string, List<int>> Mentions(AnalysisReport report, DatabaseSchema schema)
    {
        var result = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < report.Sections.Count; i++)
        {
            if (report.Sections[i].IsError) continue;
            foreach (string table in MentionedTables(report.Sections[i].Content, schema))
            {
                if (!result.TryGetValue(table, out var list)) result[table] = list = new List<int>();
                list.Add(i);
            }
        }
        return result;
    }

    private static string? Find(string name, DatabaseSchema schema)
    {
        var exact = schema.Tables.FirstOrDefault(t => string.Equals(t.FullName, name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact.FullName;

        // "dbo.Orders" for a table shown as "Orders", or a bare name that is unique across schemas
        var byName = schema.Tables.Where(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)
                                              || string.Equals(TableInfo.Qualify(t.Schema, t.Name, null), name, StringComparison.OrdinalIgnoreCase))
                                  .ToList();
        return byName.Count == 1 ? byName[0].FullName : null;
    }

    /// <summary>Removes quoting and brackets: [dbo].[Orders], "public"."orders", `orders`.</summary>
    private static string Clean(string token)
    {
        var sb = new System.Text.StringBuilder(token.Length);
        foreach (char c in token.Trim())
            if (c is not ('[' or ']' or '"' or '`')) sb.Append(c);
        return sb.ToString().Trim();
    }
}
