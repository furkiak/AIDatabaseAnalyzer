using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using System.Data.Common;
using System.Text;
using System.Text.Json;

namespace AIDatabaseAnalyzer.Services;

public class DbSchemaService : IDbSchemaService
{
    public enum DbEngine { SqlServer, PostgreSql, MySql }

    private DbConnection GetConnection(DbEngine engine, string connString) => engine switch
    {
        DbEngine.SqlServer => new SqlConnection(connString),
        DbEngine.PostgreSql => new NpgsqlConnection(connString),
        _ => new MySqlConnection(connString)
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
        string q = engine switch { DbEngine.SqlServer => "SELECT name FROM sys.tables ORDER BY name", DbEngine.PostgreSql => "SELECT tablename FROM pg_catalog.pg_tables WHERE schemaname = 'public'", _ => "SHOW TABLES" };
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

        using (var reader = await cmd.ExecuteReaderAsync())
        {
         
            var tables = new Dictionary<string, List<object>>();
            while (await reader.ReadAsync())
            {
                string tName = reader.GetString(0);
                if (filter.SelectedTables.Contains(tName))
                {
                    if (!tables.ContainsKey(tName)) tables[tName] = new List<object>();
                    tables[tName].Add(new { Column = reader.GetString(1), Type = reader.GetString(2), MaxLen = reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3)) });
                }
            }
            schemaData["Tables"] = tables;

         
            await reader.NextResultAsync();
            var fks = new List<object>();
            while (await reader.ReadAsync()) if (filter.SelectedTables.Contains(reader.GetString(1))) fks.Add(new { From = reader.GetString(1), To = reader.GetString(3), Name = reader.GetString(0) });
            schemaData["ForeignKeys"] = fks;

          
            await reader.NextResultAsync();
            if (filter.IncludeIndexes)
            {
                var idx = new List<object>();
                while (await reader.ReadAsync()) if (filter.SelectedTables.Contains(reader.GetString(0))) idx.Add(new { Table = reader.GetString(0), Name = reader.GetString(1), Unique = !reader.IsDBNull(3) && Convert.ToBoolean(reader.GetValue(3)) });
                schemaData["Indexes"] = idx;
            }

           
            await reader.NextResultAsync();
            if (filter.IncludeTriggers)
            {
                var trg = new List<object>();
                while (await reader.ReadAsync()) if (filter.SelectedTables.Contains(reader.GetString(1))) trg.Add(new { Name = reader.GetString(0), Table = reader.GetString(1) });
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
                var cns = new List<object>();
                while (await reader.ReadAsync()) if (filter.SelectedTables.Contains(reader.GetString(0))) cns.Add(new { Table = reader.GetString(0), Name = reader.GetString(1), Type = reader.GetString(2) });
                schemaData["Constraints"] = cns;
            }
        }

       
        if (filter.IncludeSampleData)
        {
            var samples = new Dictionary<string, List<Dictionary<string, object>>>();
            foreach (var table in filter.SelectedTables)
            {
                string sQ = engine switch { DbEngine.SqlServer => $"SELECT TOP 3 * FROM [{table}] ORDER BY NEWID()", _ => $"SELECT * FROM {table} LIMIT 3" };
                try
                {
                    using var sCmd = conn.CreateCommand(); sCmd.CommandText = sQ;
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
                catch { }
            }
            schemaData["SampleData"] = samples;
        }

        return JsonSerializer.Serialize(schemaData, new JsonSerializerOptions { WriteIndented = true });
    }

    public string ConvertJsonToMermaid(string jsonSchema)
    {
        var root = JsonDocument.Parse(jsonSchema).RootElement;
        var sb = new StringBuilder("erDiagram\n");
        if (root.TryGetProperty("Tables", out var tbs)) foreach (var t in tbs.EnumerateObject())
            {
                sb.AppendLine($"  {t.Name.Replace(" ", "_")} {{");
                foreach (var c in t.Value.EnumerateArray()) sb.AppendLine($"    {c.GetProperty("Type").GetString().Split(' ')[0]} {c.GetProperty("Column").GetString().Replace(" ", "_")}");
                sb.AppendLine("  }");
            }
        return sb.ToString();
    }
}