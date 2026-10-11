using System.Text.Json;
using System.Text.Json.Serialization;
using AIDatabaseAnalyzer.Models;
using AIDatabaseAnalyzer.Services.Insights;

namespace AIDatabaseAnalyzer.Services.History;

/// <summary>The header of a saved schema version; small, so the history and the health trend load instantly.</summary>
public sealed class SnapshotSummary
{
    public string Id { get; set; } = "";
    public string DatabaseName { get; set; } = "";
    public int Engine { get; set; }
    public string Server { get; set; } = "";
    public DateTime TakenAtUtc { get; set; }
    public string Fingerprint { get; set; } = "";
    public int HealthScore { get; set; }
    public string HealthGrade { get; set; } = "";
    public int TableCount { get; set; }
    public int ColumnCount { get; set; }
    public bool IsPartial { get; set; }
    public string Label { get; set; } = "";

    /// <summary>Snapshots of the same database on the same server belong together.</summary>
    public string Identity => SnapshotStore.Identity((DbEngine)Engine, Server, DatabaseName);
}

public sealed class SchemaSnapshot
{
    public SnapshotSummary Summary { get; set; } = new();
    public DatabaseSchema Schema { get; set; } = new();
}

[JsonSerializable(typeof(SchemaSnapshot))]
[JsonSerializable(typeof(SnapshotSummary))]
[JsonSourceGenerationOptions(WriteIndented = false, PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class SnapshotJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Keeps versions of extracted schemas (without sample rows) so drift between extractions can be shown
/// and the health score can be followed over time.
/// </summary>
public sealed class SnapshotStore
{
    public SnapshotStore(string folder)
    {
        Folder = folder;
        Directory.CreateDirectory(Folder);
    }

    public string Folder { get; }

    public static string Identity(DbEngine engine, string server, string database) =>
        $"{(int)engine}|{server.Trim().ToLowerInvariant()}|{database}";

    /// <summary>Saves a version unless it is identical (same structure) to the latest one of this database.</summary>
    /// <returns>The new snapshot, or the existing latest one when nothing changed.</returns>
    public async Task<SnapshotSummary> SaveAsync(DatabaseSchema schema, string server, SchemaInsights? insights, string label = "", bool skipIfUnchanged = true)
    {
        string fingerprint = SchemaDiff.Fingerprint(schema);
        if (skipIfUnchanged
            && await LatestAsync(schema.Engine, server, schema.DatabaseName, includePartial: schema.IsPartial).ConfigureAwait(false) is { } latest
            && latest.Fingerprint == fingerprint && latest.IsPartial == schema.IsPartial)
            return latest;

        var summary = new SnapshotSummary
        {
            Id = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + Guid.NewGuid().ToString("N")[..6],
            DatabaseName = schema.DatabaseName,
            Engine = (int)schema.Engine,
            Server = server.Trim(),
            TakenAtUtc = DateTime.UtcNow,
            Fingerprint = fingerprint,
            HealthScore = insights?.Score ?? 0,
            HealthGrade = insights?.Grade ?? "",
            TableCount = schema.Tables.Count,
            ColumnCount = schema.ColumnCount,
            IsPartial = schema.IsPartial,
            Label = label.Trim()
        };

        var snapshot = new SchemaSnapshot { Summary = summary, Schema = WithoutSamples(schema) };
        await WriteAtomicAsync(Path.Combine(Folder, summary.Id + ".json"), JsonSerializer.Serialize(snapshot, SnapshotJsonContext.Default.SchemaSnapshot)).ConfigureAwait(false);
        await WriteAtomicAsync(Path.Combine(Folder, summary.Id + ".meta.json"), JsonSerializer.Serialize(summary, SnapshotJsonContext.Default.SnapshotSummary)).ConfigureAwait(false);
        return summary;
    }

    public async Task<IReadOnlyList<SnapshotSummary>> ListAsync()
    {
        var result = new List<SnapshotSummary>();
        if (!Directory.Exists(Folder)) return result;
        foreach (string file in Directory.EnumerateFiles(Folder, "*.meta.json"))
        {
            try
            {
                var summary = JsonSerializer.Deserialize(await File.ReadAllTextAsync(file).ConfigureAwait(false), SnapshotJsonContext.Default.SnapshotSummary);
                if (summary is not null && summary.Id.Length > 0) result.Add(summary);
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // a damaged entry must not hide the others
            }
        }
        return result.OrderByDescending(s => s.TakenAtUtc).ToList();
    }

    public async Task<SnapshotSummary?> LatestAsync(DbEngine engine, string server, string database, bool includePartial = false)
    {
        string identity = Identity(engine, server, database);
        return (await ListAsync().ConfigureAwait(false)).FirstOrDefault(s => s.Identity == identity && (includePartial || !s.IsPartial));
    }

    public async Task<DatabaseSchema?> LoadAsync(string id)
    {
        string path = Path.Combine(Folder, SafeId(id) + ".json");
        if (!File.Exists(path)) return null;
        try
        {
            var snapshot = JsonSerializer.Deserialize(await File.ReadAllTextAsync(path).ConfigureAwait(false), SnapshotJsonContext.Default.SchemaSnapshot);
            return snapshot?.Schema;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    public Task DeleteAsync(string id)
    {
        foreach (string suffix in new[] { ".json", ".meta.json" })
        {
            try
            {
                string path = Path.Combine(Folder, SafeId(id) + suffix);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
                // removed next time
            }
        }
        return Task.CompletedTask;
    }

    private static DatabaseSchema WithoutSamples(DatabaseSchema s)
    {
        var copy = new DatabaseSchema
        {
            DatabaseName = s.DatabaseName,
            Engine = s.Engine,
            ServerVersion = s.ServerVersion,
            ExtractedAtUtc = s.ExtractedAtUtc,
            IndexesIncluded = s.IndexesIncluded,
            ViewsIncluded = s.ViewsIncluded,
            TriggersIncluded = s.TriggersIncluded,
            RoutinesIncluded = s.RoutinesIncluded,
            ConstraintsIncluded = s.ConstraintsIncluded,
            DefinitionsIncluded = s.DefinitionsIncluded,
            IsPartial = s.IsPartial
        };
        copy.Tables.AddRange(s.Tables);
        copy.ForeignKeys.AddRange(s.ForeignKeys);
        copy.Indexes.AddRange(s.Indexes);
        copy.Triggers.AddRange(s.Triggers);
        copy.Views.AddRange(s.Views);
        copy.Routines.AddRange(s.Routines);
        copy.Constraints.AddRange(s.Constraints);
        return copy;
    }

    private static string SafeId(string id) => new(id.Where(char.IsLetterOrDigit).ToArray());

    private static async Task WriteAtomicAsync(string path, string content)
    {
        string temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, content).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
    }
}
