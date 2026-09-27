using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using System.Data.Common;
using System.Text.Json;

namespace AIDatabaseAnalyzer.Services;

public class DbSchemaService : IDbSchemaService
{
    private DbConnection GetConnection(DbEngine engine, string connString) => engine switch
    {
        DbEngine.SqlServer => new SqlConnection(connString),
        DbEngine.PostgreSql => new NpgsqlConnection(connString),
        DbEngine.MySql => new MySqlConnection(connString),
        _ => throw new ArgumentOutOfRangeException(nameof(engine))
    };

    public async Task<List<string>> GetDatabasesAsync(DbEngine engine, string connString)
    {
        var dbs = new List<string>();
        string q = engine switch { DbEngine.SqlServer => "SELECT name FROM sys.databases WHERE database_id > 4", DbEngine.PostgreSql => "SELECT datname FROM pg_database WHERE datistemplate = false", _ => "SHOW DATABASES" };
        using var conn = GetConnection(engine, connString); await conn.OpenAsync();
        using var cmd = conn.CreateCommand(); cmd.CommandText = q;
        using var r = await cmd.ExecuteReaderAsync(); while (await r.ReadAsync()) dbs.Add(r.GetString(0));
        return dbs;
    }

    public async Task<List<string>> GetTablesAsync(DbEngine engine, string connString)
        => (await GetTableIdentitiesAsync(engine, connString)).Select(identity => identity.Key).ToList();

    public async Task<List<DatabaseObjectIdentity>> GetTableIdentitiesAsync(DbEngine engine, string connString)
    {
        var tbs = new List<DatabaseObjectIdentity>();
        string q = SqlQueries.TableList(engine);
        using var conn = GetConnection(engine, connString); await conn.OpenAsync();
        using var cmd = conn.CreateCommand(); cmd.CommandText = q;
        using var r = await cmd.ExecuteReaderAsync(); while (await r.ReadAsync()) tbs.Add(ReadIdentity(r, 0));
        return tbs;
    }

    public async Task<string> ExtractSchemaAsJsonAsync(DbEngine engine, string connectionString, SchemaFilterConfig filter)
    {
        var schemaData = new Dictionary<string, object>
        {
            ["MetadataFormat"] = new { Version = 2, IdentityFields = new[] { "Schema", "Name" },
                KeyFormat = "schema.name; bracket-quote both parts if either contains dot or brackets; escape closing bracket as ]]",
                ObjectScope = "Selected tables and their outgoing relationships; visible views and routines are database-wide when enabled" }
        };
        string grandQuery = engine switch { DbEngine.SqlServer => SqlQueries.SqlServerSchema, DbEngine.PostgreSql => SqlQueries.PostgreSqlSchema, _ => SqlQueries.MySqlSchema };

        var selectedTables = filter.SelectedTableIdentities.Concat(filter.SelectedTables.Select(DatabaseObjectIdentity.ParseKey)).ToHashSet();
        using var conn = GetConnection(engine, connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand(); cmd.CommandText = grandQuery;
        var knownTables = new HashSet<DatabaseObjectIdentity>();

        using (var reader = await cmd.ExecuteReaderAsync())
        {
         
            var tables = new Dictionary<string, List<ColumnMetadata>>(StringComparer.Ordinal);
            var tableIdentities = new Dictionary<string, DatabaseObjectIdentity>(StringComparer.Ordinal);
            while (await reader.ReadAsync())
            {
                var identity = ReadIdentity(reader, 0);
                if (selectedTables.Contains(identity))
                {
                    var tName = identity.Key;
                    knownTables.Add(identity);
                    tableIdentities[tName] = identity;
                    if (!tables.ContainsKey(tName)) tables[tName] = new List<ColumnMetadata>();
                    tables[tName].Add(new ColumnMetadata(reader.GetString(2), reader.GetString(3),
                        reader.IsDBNull(4) ? null : Convert.ToInt32(reader.GetValue(4)),
                        Convert.ToBoolean(reader.GetValue(5)), Convert.ToBoolean(reader.GetValue(6))));
                }
            }
            schemaData["Tables"] = tables;
            schemaData["TableIdentities"] = tableIdentities;

         
            await reader.NextResultAsync();
            var fks = new Dictionary<(string Name, DatabaseObjectIdentity From, DatabaseObjectIdentity To), ForeignKeyMetadata>();
            while (await reader.ReadAsync())
            {
                var from = ReadIdentity(reader, 1);
                if (!selectedTables.Contains(from)) continue;
                var key = (Name: reader.GetString(0), From: from, To: ReadIdentity(reader, 4));
                if (!fks.TryGetValue(key, out var fk))
                    fks[key] = fk = new ForeignKeyMetadata { Name = key.Name, FromIdentity = key.From, ToIdentity = key.To };
                fk.Columns.Add(new ForeignKeyColumn(reader.GetString(3), reader.GetString(6), Convert.ToInt32(reader.GetValue(7))));
            }
            foreach (var fk in fks.Values) fk.Columns.Sort((a, b) => a.Ordinal.CompareTo(b.Ordinal));
            schemaData["ForeignKeys"] = fks.Values.ToList();

          
            await reader.NextResultAsync();
            if (filter.IncludeIndexes)
            {
                var indexes = new Dictionary<(DatabaseObjectIdentity Table, string Name), IndexMetadata>();
                while (await reader.ReadAsync())
                {
                    var table = ReadIdentity(reader, 0);
                    if (!selectedTables.Contains(table)) continue;
                    var key = (Table: table, Name: reader.GetString(2));
                    if (!indexes.TryGetValue(key, out var index))
                        indexes[key] = index = new IndexMetadata
                        {
                            TableIdentity = table, Name = key.Name, Type = reader.GetString(3),
                            Unique = Convert.ToBoolean(reader.GetValue(4)),
                            Filter = reader.IsDBNull(8) ? null : reader.GetString(8),
                            Definition = reader.IsDBNull(9) ? null : reader.GetString(9)
                        };
                    index.Columns.Add(new IndexColumn(reader.IsDBNull(5) ? null : reader.GetString(5),
                        reader.IsDBNull(6) ? 0 : Convert.ToInt32(reader.GetValue(6)),
                        !reader.IsDBNull(7) && Convert.ToBoolean(reader.GetValue(7))));
                }
                foreach (var index in indexes.Values)
                    index.Columns.Sort((a, b) => a.Included == b.Included
                        ? a.Ordinal.CompareTo(b.Ordinal) : a.Included.CompareTo(b.Included));
                schemaData["Indexes"] = indexes.Values.ToList();
            }

           
            await reader.NextResultAsync();
            if (filter.IncludeTriggers)
            {
                var trg = new List<TriggerMetadata>();
                while (await reader.ReadAsync())
                {
                    var table = ReadIdentity(reader, 2);
                    if (selectedTables.Contains(table)) trg.Add(new TriggerMetadata(
                        new DatabaseObjectIdentity(reader.GetString(1), reader.GetString(0)), table,
                        reader.IsDBNull(4) ? null : Convert.ToBoolean(reader.GetValue(4))));
                }
                schemaData["Triggers"] = trg;
            }

         
            await reader.NextResultAsync();
            if (filter.IncludeViews)
            {
                var vws = new List<string>();
                var identities = new Dictionary<string, DatabaseObjectIdentity>(StringComparer.Ordinal);
                while (await reader.ReadAsync())
                {
                    var identity = ReadIdentity(reader, 0);
                    vws.Add(identity.Key);
                    identities[identity.Key] = identity;
                }
                schemaData["Views"] = vws;
                schemaData["ViewIdentities"] = identities;
            }

           
            await reader.NextResultAsync();
            if (filter.IncludeSps)
            {
                var sps = new Dictionary<string, List<string>>();
                var identities = new Dictionary<string, DatabaseObjectIdentity>(StringComparer.Ordinal);
                while (await reader.ReadAsync())
                {
                    var identity = ReadIdentity(reader, 0);
                    string sName = identity.Key;
                    identities[sName] = identity;
                    if (!sps.ContainsKey(sName)) sps[sName] = new List<string>();
                    if (!reader.IsDBNull(2)) sps[sName].Add(reader.GetString(2));
                }
                schemaData["StoredProcedures"] = sps;
                schemaData["RoutineIdentities"] = identities;
            }

          
            await reader.NextResultAsync();
            if (filter.IncludeConstraints)
            {
                var cns = new List<ConstraintMetadata>();
                while (await reader.ReadAsync())
                {
                    var table = ReadIdentity(reader, 0);
                    if (selectedTables.Contains(table)) cns.Add(new ConstraintMetadata(table,
                        reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4)));
                }
                schemaData["Constraints"] = cns;
            }
        }

       
        if (filter.IncludeSampleData)
        {
            var samples = new Dictionary<string, List<Dictionary<string, object?>>>();
            var sampleErrors = new List<string>();
            foreach (var table in selectedTables)
            {
                if (!knownTables.Contains(table))
                {
                    sampleErrors.Add($"{table.Key}: not found in selected table metadata");
                    continue;
                }
                string sQ = SampleQuery(engine, table);
                try
                {
                    using var sCmd = conn.CreateCommand(); sCmd.CommandText = sQ; sCmd.CommandTimeout = 5;
                    using var sR = await sCmd.ExecuteReaderAsync();
                    var rows = new List<Dictionary<string, object?>>();
                    while (await sR.ReadAsync())
                    {
                        var row = new Dictionary<string, object?>();
                        for (int i = 0; i < sR.FieldCount; i++) row[sR.GetName(i)] = sR.IsDBNull(i) ? null : sR.GetValue(i);
                        rows.Add(row);
                    }
                    samples[table.Key] = rows;
                }
                catch (DbException) { sampleErrors.Add($"{table.Key}: sample unavailable (permission, timeout or provider error)"); }
            }
            schemaData["SampleData"] = samples;
            if (sampleErrors.Count > 0) schemaData["SampleErrors"] = sampleErrors;
        }

        return JsonSerializer.Serialize(schemaData, new JsonSerializerOptions { WriteIndented = true });
    }

    private static DatabaseObjectIdentity ReadIdentity(DbDataReader reader, int schemaOrdinal) =>
        new(reader.GetString(schemaOrdinal), reader.GetString(schemaOrdinal + 1));

    public static string SampleQuery(DbEngine engine, DatabaseObjectIdentity table)
    {
        var quoted = QuoteTable(engine, table.Schema, table.Name);
        return engine == DbEngine.SqlServer ? $"SELECT TOP 3 * FROM {quoted}" : $"SELECT * FROM {quoted} LIMIT 3";
    }

    public static string QuoteTable(DbEngine engine, string schema, string table) => engine switch
    {
        DbEngine.SqlServer => $"[{schema.Replace("]", "]]", StringComparison.Ordinal)}].[{table.Replace("]", "]]", StringComparison.Ordinal)}]",
        DbEngine.PostgreSql => $"\"{schema.Replace("\"", "\"\"", StringComparison.Ordinal)}\".\"{table.Replace("\"", "\"\"", StringComparison.Ordinal)}\"",
        DbEngine.MySql => $"`{schema.Replace("`", "``", StringComparison.Ordinal)}`.`{table.Replace("`", "``", StringComparison.Ordinal)}`",
        _ => throw new ArgumentOutOfRangeException(nameof(engine))
    };

}
