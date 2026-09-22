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
    {
        var tbs = new List<string>();
        string q = engine switch
        {
            DbEngine.SqlServer => "SELECT s.name + '.' + t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id ORDER BY s.name, t.name",
            DbEngine.PostgreSql => "SELECT schemaname || '.' || tablename FROM pg_catalog.pg_tables WHERE schemaname NOT IN ('pg_catalog', 'information_schema') ORDER BY schemaname, tablename",
            _ => "SELECT CONCAT(TABLE_SCHEMA, '.', TABLE_NAME) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME"
        };
        using var conn = GetConnection(engine, connString); await conn.OpenAsync();
        using var cmd = conn.CreateCommand(); cmd.CommandText = q;
        using var r = await cmd.ExecuteReaderAsync(); while (await r.ReadAsync()) tbs.Add(r.GetString(0));
        return tbs;
    }

    public async Task<string> ExtractSchemaAsJsonAsync(DbEngine engine, string connectionString, SchemaFilterConfig filter)
    {
        var schemaData = new Dictionary<string, object>();
        string grandQuery = engine switch { DbEngine.SqlServer => SqlQueries.SqlServerSchema, DbEngine.PostgreSql => SqlQueries.PostgreSqlSchema, _ => SqlQueries.MySqlSchema };

        using var conn = GetConnection(engine, connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand(); cmd.CommandText = grandQuery;
        var selectedTables = filter.SelectedTables.ToHashSet(StringComparer.Ordinal);
        var quotedTables = new Dictionary<string, string>(StringComparer.Ordinal);

        using (var reader = await cmd.ExecuteReaderAsync())
        {
         
            var tables = new Dictionary<string, List<ColumnMetadata>>(StringComparer.Ordinal);
            while (await reader.ReadAsync())
            {
                string tName = reader.GetString(0);
                if (selectedTables.Contains(tName))
                {
                    var quotedName = QuoteTable(engine, reader.GetString(4), reader.GetString(5));
                    if (quotedTables.TryGetValue(tName, out var existingName) && existingName != quotedName)
                        throw new InvalidOperationException("Two tables have the same display name. Narrow the schema scope.");
                    quotedTables[tName] = quotedName;
                    if (!tables.ContainsKey(tName)) tables[tName] = new List<ColumnMetadata>();
                    tables[tName].Add(new ColumnMetadata(reader.GetString(1), reader.GetString(2),
                        reader.IsDBNull(3) ? null : Convert.ToInt32(reader.GetValue(3)),
                        Convert.ToBoolean(reader.GetValue(6)), Convert.ToBoolean(reader.GetValue(7))));
                }
            }
            schemaData["Tables"] = tables;

         
            await reader.NextResultAsync();
            var fks = new Dictionary<(string Name, string From, string To), ForeignKeyMetadata>();
            while (await reader.ReadAsync())
            {
                var from = reader.GetString(1);
                if (!selectedTables.Contains(from)) continue;
                var key = (Name: reader.GetString(0), From: from, To: reader.GetString(3));
                if (!fks.TryGetValue(key, out var fk))
                    fks[key] = fk = new ForeignKeyMetadata { Name = key.Name, From = key.From, To = key.To };
                fk.Columns.Add(new ForeignKeyColumn(reader.GetString(2), reader.GetString(4), Convert.ToInt32(reader.GetValue(5))));
            }
            foreach (var fk in fks.Values) fk.Columns.Sort((a, b) => a.Ordinal.CompareTo(b.Ordinal));
            schemaData["ForeignKeys"] = fks.Values.ToList();

          
            await reader.NextResultAsync();
            if (filter.IncludeIndexes)
            {
                var indexes = new Dictionary<(string Table, string Name), IndexMetadata>();
                while (await reader.ReadAsync())
                {
                    var table = reader.GetString(0);
                    if (!selectedTables.Contains(table)) continue;
                    var key = (Table: table, Name: reader.GetString(1));
                    if (!indexes.TryGetValue(key, out var index))
                        indexes[key] = index = new IndexMetadata
                        {
                            Table = table, Name = key.Name, Type = reader.GetString(2),
                            Unique = Convert.ToBoolean(reader.GetValue(3)),
                            Filter = reader.IsDBNull(7) ? null : reader.GetString(7),
                            Definition = reader.IsDBNull(8) ? null : reader.GetString(8)
                        };
                    index.Columns.Add(new IndexColumn(reader.IsDBNull(4) ? null : reader.GetString(4),
                        reader.IsDBNull(5) ? 0 : Convert.ToInt32(reader.GetValue(5)),
                        !reader.IsDBNull(6) && Convert.ToBoolean(reader.GetValue(6))));
                }
                foreach (var index in indexes.Values)
                    index.Columns.Sort((a, b) => a.Included == b.Included
                        ? a.Ordinal.CompareTo(b.Ordinal) : a.Included.CompareTo(b.Included));
                schemaData["Indexes"] = indexes.Values.ToList();
            }

           
            await reader.NextResultAsync();
            if (filter.IncludeTriggers)
            {
                var trg = new List<object>();
                while (await reader.ReadAsync()) if (selectedTables.Contains(reader.GetString(1))) trg.Add(new { Name = reader.GetString(0), Table = reader.GetString(1) });
                schemaData["Triggers"] = trg;
            }

         
            await reader.NextResultAsync();
            if (filter.IncludeViews)
            {
                var vws = new List<string>();
                while (await reader.ReadAsync()) vws.Add(reader.GetString(0));
                schemaData["Views"] = vws;
            }

           
            await reader.NextResultAsync();
            if (filter.IncludeSps)
            {
                var sps = new Dictionary<string, List<string>>();
                while (await reader.ReadAsync())
                {
                    string sName = reader.GetString(0);
                    if (!sps.ContainsKey(sName)) sps[sName] = new List<string>();
                    if (!reader.IsDBNull(1)) sps[sName].Add(reader.GetString(1));
                }
                schemaData["StoredProcedures"] = sps;
            }

          
            await reader.NextResultAsync();
            if (filter.IncludeConstraints)
            {
                var cns = new List<ConstraintMetadata>();
                while (await reader.ReadAsync()) if (selectedTables.Contains(reader.GetString(0)))
                    cns.Add(new ConstraintMetadata(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3)));
                schemaData["Constraints"] = cns;
            }
        }

       
        if (filter.IncludeSampleData)
        {
            var samples = new Dictionary<string, List<Dictionary<string, object>>>();
            var sampleErrors = new List<string>();
            foreach (var table in selectedTables)
            {
                if (!quotedTables.TryGetValue(table, out var quotedTable))
                {
                    sampleErrors.Add($"{table}: not found in selected table metadata");
                    continue;
                }
                string sQ = engine == DbEngine.SqlServer ? $"SELECT TOP 3 * FROM {quotedTable}" : $"SELECT * FROM {quotedTable} LIMIT 3";
                try
                {
                    using var sCmd = conn.CreateCommand(); sCmd.CommandText = sQ; sCmd.CommandTimeout = 5;
                    using var sR = await sCmd.ExecuteReaderAsync();
                    var rows = new List<Dictionary<string, object>>();
                    while (await sR.ReadAsync())
                    {
                        var row = new Dictionary<string, object>();
                        for (int i = 0; i < sR.FieldCount; i++) row[sR.GetName(i)] = sR.IsDBNull(i) ? null : sR.GetValue(i);
                        rows.Add(row);
                    }
                    samples[table] = rows;
                }
                catch (DbException) { sampleErrors.Add($"{table}: sample unavailable (permission, timeout or provider error)"); }
            }
            schemaData["SampleData"] = samples;
            if (sampleErrors.Count > 0) schemaData["SampleErrors"] = sampleErrors;
        }

        return JsonSerializer.Serialize(schemaData, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string QuoteTable(DbEngine engine, string schema, string table) => engine switch
    {
        DbEngine.SqlServer => $"[{schema.Replace("]", "]]", StringComparison.Ordinal)}].[{table.Replace("]", "]]", StringComparison.Ordinal)}]",
        DbEngine.PostgreSql => $"\"{schema.Replace("\"", "\"\"", StringComparison.Ordinal)}\".\"{table.Replace("\"", "\"\"", StringComparison.Ordinal)}\"",
        DbEngine.MySql => $"`{schema.Replace("`", "``", StringComparison.Ordinal)}`.`{table.Replace("`", "``", StringComparison.Ordinal)}`",
        _ => throw new ArgumentOutOfRangeException(nameof(engine))
    };

}
