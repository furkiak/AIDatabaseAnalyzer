using System.Data;
using System.Data.Common;
using System.Globalization;
using AIDatabaseAnalyzer.Models;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;

namespace AIDatabaseAnalyzer.Services.Database;

/// <summary>Reads database metadata (and optional sample rows) from SQL Server, PostgreSQL, MySQL/MariaDB, Oracle and SQLite.</summary>
public sealed class DbSchemaService : IDbSchemaService
{
    private const int MaxSampledColumns = 50;
    private const int SampleCommandTimeoutSeconds = 30;

    // ------------------------------------------------------------------ connections

    public static string BuildConnectionString(ConnectionSettings s, string? database)
    {
        int timeout = Math.Clamp(s.TimeoutSeconds, 3, 120);

        switch (s.Engine)
        {
            case DbEngine.SqlServer:
            {
                var b = new SqlConnectionStringBuilder
                {
                    DataSource = s.Port is > 0 ? $"{s.Server},{s.Port}" : s.Server,
                    ConnectTimeout = timeout,
                    ApplicationName = "AI Database Analyzer",
                    TrustServerCertificate = s.TrustServerCertificate
                };
                if (!string.IsNullOrWhiteSpace(database)) b.InitialCatalog = database;
                if (s.UseIntegratedSecurity)
                {
                    b.IntegratedSecurity = true;
                }
                else
                {
                    b.UserID = s.Username;
                    b.Password = s.Password;
                }
                return b.ConnectionString;
            }

            case DbEngine.PostgreSql:
            {
                var b = new NpgsqlConnectionStringBuilder
                {
                    Host = s.Server,
                    Port = s.Port is > 0 ? s.Port.Value : 5432,
                    Username = s.Username,
                    Password = s.Password,
                    Database = string.IsNullOrWhiteSpace(database) ? "postgres" : database,
                    Timeout = timeout,
                    CommandTimeout = 60,
                    ApplicationName = "AI Database Analyzer"
                };
                return b.ConnectionString;
            }

            case DbEngine.Oracle:
            {
                var b = new OracleConnectionStringBuilder
                {
                    DataSource = OracleDataSource(s),
                    UserID = s.Username,
                    Password = s.Password,
                    ConnectionTimeout = timeout
                };
                return b.ConnectionString;
            }

            case DbEngine.Sqlite:
            {
                // Read-only and unpooled: the file is never modified and not kept open after the analysis.
                var b = new SqliteConnectionStringBuilder
                {
                    DataSource = s.Server.Trim(),
                    Mode = SqliteOpenMode.ReadOnly,
                    Pooling = false,
                    DefaultTimeout = 30
                };
                return b.ConnectionString;
            }

            default:
            {
                var b = new MySqlConnectionStringBuilder
                {
                    Server = s.Server,
                    Port = (uint)(s.Port is > 0 ? s.Port.Value : 3306),
                    UserID = s.Username,
                    Password = s.Password,
                    ConnectionTimeout = (uint)timeout,
                    DefaultCommandTimeout = 60,
                    AllowPublicKeyRetrieval = s.TrustServerCertificate
                };
                if (!string.IsNullOrWhiteSpace(database)) b.Database = database;
                return b.ConnectionString;
            }
        }
    }

    /// <summary>"host:port/service" (EZConnect), or the server field as-is when it already is a descriptor or a full address.</summary>
    internal static string OracleDataSource(ConnectionSettings s)
    {
        string server = s.Server.Trim();
        if (server.Contains('(') || server.Contains('/') || server.Contains("://", StringComparison.Ordinal)) return server;
        int port = s.Port is > 0 ? s.Port.Value : 1521;
        string service = string.IsNullOrWhiteSpace(s.ServiceName) ? "FREEPDB1" : s.ServiceName.Trim();
        return $"{server}:{port}/{service}";
    }

    private static DbConnection CreateConnection(ConnectionSettings s, string? database)
    {
        string cs = BuildConnectionString(s, database);
        return s.Engine switch
        {
            DbEngine.SqlServer => new SqlConnection(cs),
            DbEngine.PostgreSql => new NpgsqlConnection(cs),
            DbEngine.Oracle => new OracleConnection(cs),
            DbEngine.Sqlite => new SqliteConnection(cs),
            _ => new MySqlConnection(cs)
        };
    }

    private static async Task<DbConnection> OpenAsync(ConnectionSettings s, string? database, CancellationToken ct)
    {
        if (s.Engine == DbEngine.Sqlite && !File.Exists(s.Server.Trim()))
            throw new FileNotFoundException($"The database file “{s.Server}” does not exist.");

        var conn = CreateConnection(s, database);
        try
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);

            // Oracle has no "database" per connection: point the session at the chosen schema so the catalog queries see it.
            if (s.Engine == DbEngine.Oracle && !string.IsNullOrWhiteSpace(database))
                await ExecuteAsync(conn, "ALTER SESSION SET CURRENT_SCHEMA = " + SqlCatalog.Quote(DbEngine.Oracle, database), ct).ConfigureAwait(false);
            if (s.Engine == DbEngine.Sqlite)
                await ExecuteAsync(conn, "PRAGMA query_only = ON", ct).ConfigureAwait(false);

            return conn;
        }
        catch
        {
            await conn.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    // ------------------------------------------------------------------ listing

    public async Task<IReadOnlyList<string>> GetDatabasesAsync(ConnectionSettings connection, CancellationToken ct = default)
    {
        if (connection.Engine == DbEngine.Sqlite)
        {
            // A SQLite file is exactly one database; open it once to prove it is a valid database file.
            await using var file = await OpenAsync(connection, null, ct).ConfigureAwait(false);
            await ExecuteAsync(file, "SELECT COUNT(*) FROM sqlite_master", ct).ConfigureAwait(false);
            return new[] { SqliteDatabaseName(connection) };
        }

        DbConnection conn;
        try
        {
            conn = await OpenAsync(connection, null, ct).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InvalidCatalogName)
        {
            // The default "postgres" maintenance database does not exist (common on managed hosts).
            conn = await OpenAsync(connection, "template1", ct).ConfigureAwait(false);
        }

        await using (conn)
        {
            var result = new List<string>();
            await ReadRowsAsync(conn, SqlCatalog.Databases(connection.Engine), r => result.Add(r.Str(0) ?? ""), ct)
                .ConfigureAwait(false);
            result.RemoveAll(string.IsNullOrWhiteSpace);
            return result;
        }
    }

    public async Task<IReadOnlyList<TableInfo>> GetTablesAsync(ConnectionSettings connection, string database, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(connection, database, ct).ConfigureAwait(false);
        var tables = new List<TableInfo>();
        string? defaultSchema = connection.Engine.DefaultSchema();

        await ReadRowsAsync(conn, SqlCatalog.Tables(connection.Engine), r =>
        {
            tables.Add(new TableInfo
            {
                Schema = r.Str(0),
                Name = r.Str(1) ?? "",
                RowCount = r.Long(2),
                DefaultSchema = defaultSchema
            });
        }, ct).ConfigureAwait(false);

        if (connection.Engine == DbEngine.Sqlite)
            await CountSqliteRowsAsync(conn, tables, ct).ConfigureAwait(false);
        return tables;
    }

    /// <summary>The file name stands in for the database name of a SQLite file.</summary>
    public static string SqliteDatabaseName(ConnectionSettings connection) =>
        Path.GetFileNameWithoutExtension(connection.Server.Trim()) is { Length: > 0 } name ? name : "main";

    // ------------------------------------------------------------------ extraction

    public async Task<DatabaseSchema> ExtractSchemaAsync(
        ConnectionSettings connection,
        string database,
        SchemaExtractionOptions options,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var engine = connection.Engine;
        string? defaultSchema = engine.DefaultSchema();
        var schema = new DatabaseSchema { DatabaseName = database, Engine = engine, IndexesIncluded = options.IncludeIndexes };

        await using var conn = await OpenAsync(connection, database, ct).ConfigureAwait(false);
        schema.ServerVersion = conn.ServerVersion;

        // --- tables (required)
        progress?.Report("Reading tables…");
        var byKey = new Dictionary<string, TableInfo>(StringComparer.Ordinal);
        await ReadRowsAsync(conn, SqlCatalog.Tables(engine), r =>
        {
            var table = new TableInfo { Schema = r.Str(0), Name = r.Str(1) ?? "", RowCount = r.Long(2), DefaultSchema = defaultSchema };
            if (options.SelectedTables is not null && !options.SelectedTables.Contains(table.FullName)) return;
            byKey[Key(table.Schema, table.Name)] = table;
            schema.Tables.Add(table);
        }, ct).ConfigureAwait(false);

        if (engine == DbEngine.Sqlite)
            await CountSqliteRowsAsync(conn, schema.Tables, ct).ConfigureAwait(false);

        // --- columns (required)
        progress?.Report("Reading columns…");
        await ReadRowsAsync(conn, SqlCatalog.Columns(engine), r =>
        {
            if (!byKey.TryGetValue(Key(r.Str(0), r.Str(1) ?? ""), out var table)) return;
            bool identity = r.Bool(7);
            string? defaultValue = NormalizeDefault(r.Str(8));
            // The sequence behind an identity column ("ISEQ$$_73345".nextval, nextval('t_id_seq')) is noise.
            if (identity && defaultValue?.Contains("nextval", StringComparison.OrdinalIgnoreCase) == true) defaultValue = null;
            table.Columns.Add(new ColumnInfo
            {
                Name = r.Str(2) ?? "",
                BaseType = r.Str(3) ?? "",
                DataType = Shorten(r.Str(4) ?? r.Str(3) ?? "", 120) ?? "",
                MaxLength = r.Long(5),
                IsNullable = r.Bool(6),
                IsIdentity = identity,
                DefaultValue = defaultValue
            });
        }, ct).ConfigureAwait(false);

        // --- indexes: always read, because primary keys are derived from them
        progress?.Report("Reading indexes and keys…");
        await TrySectionAsync(schema, "Indexes / primary keys", async () =>
        {
            IndexInfo? current = null;
            string currentKey = "";
            await ReadRowsAsync(conn, SqlCatalog.Indexes(engine), r =>
            {
                string? sch = r.Str(0);
                string tbl = r.Str(1) ?? "";
                if (!byKey.TryGetValue(Key(sch, tbl), out var table)) return;

                string indexName = r.Str(2) ?? "";
                string key = Key(sch, tbl) + "|" + indexName;
                if (current is null || key != currentKey)
                {
                    current = new IndexInfo
                    {
                        Table = table.FullName,
                        Name = indexName,
                        Type = r.Str(3),
                        IsUnique = r.Bool(4),
                        IsPrimary = r.Bool(5),
                        Filter = Shorten(r.Str(9), 200)
                    };
                    currentKey = key;
                    if (options.IncludeIndexes || current.IsPrimary) schema.Indexes.Add(current);
                }

                string column = r.Str(6) ?? "";
                if (r.Bool(7)) current.IncludedColumns.Add(column);
                else current.Columns.Add(column);
            }, ct).ConfigureAwait(false);

            foreach (var pk in schema.Indexes.Where(i => i.IsPrimary))
            {
                var table = schema.FindTable(pk.Table);
                if (table is null) continue;
                foreach (var col in table.Columns.Where(c => pk.Columns.Contains(c.Name, StringComparer.Ordinal)))
                    col.IsPrimaryKey = true;
            }

            if (!options.IncludeIndexes) schema.Indexes.Clear();
        }).ConfigureAwait(false);

        // --- foreign keys
        progress?.Report("Reading relationships…");
        await TrySectionAsync(schema, "Foreign keys", async () =>
        {
            ForeignKeyInfo? current = null;
            string currentKey = "";
            await ReadRowsAsync(conn, SqlCatalog.ForeignKeys(engine), r =>
            {
                string? fromSchema = r.Str(1);
                string fromTable = r.Str(2) ?? "";
                if (!byKey.ContainsKey(Key(fromSchema, fromTable))) return;

                string name = r.Str(0) ?? "";
                string key = Key(fromSchema, fromTable) + "|" + name;
                if (current is null || key != currentKey)
                {
                    current = new ForeignKeyInfo
                    {
                        Name = name,
                        FromTable = TableInfo.Qualify(fromSchema, fromTable, defaultSchema),
                        ToTable = TableInfo.Qualify(r.Str(4), r.Str(5) ?? "", defaultSchema),
                        OnDelete = NormalizeAction(r.Str(7)),
                        OnUpdate = NormalizeAction(r.Str(8))
                    };
                    currentKey = key;
                    schema.ForeignKeys.Add(current);
                }

                current.FromColumns.Add(r.Str(3) ?? "");
                current.ToColumns.Add(r.Str(6) ?? "");
            }, ct).ConfigureAwait(false);
        }).ConfigureAwait(false);

        // --- triggers
        if (options.IncludeTriggers)
        {
            progress?.Report("Reading triggers…");
            await TrySectionAsync(schema, "Triggers", async () =>
            {
                await ReadRowsAsync(conn, SqlCatalog.Triggers(engine, options.IncludeDefinitions, options.MaxDefinitionChars), r =>
                {
                    if (!byKey.TryGetValue(Key(r.Str(1), r.Str(2) ?? ""), out var table)) return;
                    schema.Triggers.Add(new TriggerInfo
                    {
                        Name = r.Str(0) ?? "",
                        Table = table.FullName,
                        Events = r.Str(3),
                        Timing = r.Str(4),
                        IsDisabled = r.Bool(5),
                        Definition = CleanDefinition(r.Str(6), options)
                    });
                }, ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        // --- views
        if (options.IncludeViews)
        {
            progress?.Report("Reading views…");
            await TrySectionAsync(schema, "Views", async () =>
            {
                await ReadRowsAsync(conn, SqlCatalog.Views(engine, options.IncludeDefinitions, options.MaxDefinitionChars), r =>
                {
                    schema.Views.Add(new ViewInfo
                    {
                        Name = TableInfo.Qualify(r.Str(0), r.Str(1) ?? "", defaultSchema),
                        Definition = CleanDefinition(r.Str(2), options)
                    });
                }, ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        // --- stored procedures and functions
        if (options.IncludeRoutines)
        {
            progress?.Report("Reading stored procedures and functions…");
            await TrySectionAsync(schema, "Routines", async () =>
            {
                await ReadRowsAsync(conn, SqlCatalog.Routines(engine, options.IncludeDefinitions, options.MaxDefinitionChars), r =>
                {
                    var routine = new RoutineInfo
                    {
                        Name = TableInfo.Qualify(r.Str(0), r.Str(1) ?? "", defaultSchema),
                        Kind = r.Str(2) ?? "PROCEDURE",
                        Definition = CleanDefinition(r.Str(4), options)
                    };
                    string? parameters = r.Str(3);
                    if (!string.IsNullOrWhiteSpace(parameters)) routine.Parameters.Add(Shorten(parameters, 300)!);
                    schema.Routines.Add(routine);
                }, ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        // --- check constraints
        if (options.IncludeConstraints && engine == DbEngine.Sqlite)
        {
            await TrySectionAsync(schema, "Constraints", async () =>
            {
                await ReadRowsAsync(conn, "SELECT name, sql FROM sqlite_master WHERE type = 'table' AND sql IS NOT NULL", r =>
                {
                    if (!byKey.TryGetValue(Key(null, r.Str(0) ?? ""), out var table)) return;
                    foreach (var (name, expression) in SqlCatalog.ParseSqliteChecks(r.Str(1) ?? ""))
                        schema.Constraints.Add(new ConstraintInfo { Table = table.FullName, Name = name, Type = "CHECK", Definition = Shorten(expression, 400) });
                }, ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        else if (options.IncludeConstraints)
        {
            progress?.Report("Reading constraints…");
            await TrySectionAsync(schema, "Constraints", async () =>
            {
                await ReadRowsAsync(conn, SqlCatalog.CheckConstraints(engine), r =>
                {
                    if (!byKey.TryGetValue(Key(r.Str(0), r.Str(1) ?? ""), out var table)) return;
                    schema.Constraints.Add(new ConstraintInfo
                    {
                        Table = table.FullName,
                        Name = r.Str(2) ?? "",
                        Type = r.Str(3) ?? "CHECK",
                        Definition = Shorten(r.Str(4), 400)
                    });
                }, ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        // --- sample rows
        if (options.IncludeSampleData)
            await ReadSampleDataAsync(conn, schema, options, progress, ct).ConfigureAwait(false);

        progress?.Report("Done.");
        return schema;
    }

    /// <summary>SQLite keeps no row statistics; count the rows, but give up after a few seconds on very large files.</summary>
    private static async Task CountSqliteRowsAsync(DbConnection conn, IEnumerable<TableInfo> tables, CancellationToken ct)
    {
        var budget = Stopwatch.StartNew();
        foreach (var table in tables)
        {
            if (budget.Elapsed > TimeSpan.FromSeconds(8)) break;
            await ReadRowsAsync(conn, $"SELECT COUNT(*) FROM {SqlCatalog.QuoteTable(DbEngine.Sqlite, table)}", r => table.RowCount = r.Long(0), ct)
                .ConfigureAwait(false);
        }
    }

    // ------------------------------------------------------------------ sample rows

    private static async Task ReadSampleDataAsync(
        DbConnection conn, DatabaseSchema schema, SchemaExtractionOptions options, IProgress<string>? progress, CancellationToken ct)
    {
        var engine = schema.Engine;
        int rowsWanted = Math.Clamp(options.SampleRowCount, 1, 10);
        int done = 0;

        if (engine == DbEngine.SqlServer)
        {
            // Never block on locked tables while sampling.
            await ExecuteAsync(conn, "SET LOCK_TIMEOUT 5000", ct).ConfigureAwait(false);
        }

        foreach (var table in schema.Tables)
        {
            ct.ThrowIfCancellationRequested();
            done++;
            if (done % 5 == 0 || done == schema.Tables.Count)
                progress?.Report($"Sampling rows… {done}/{schema.Tables.Count}");

            var columns = table.Columns
                .Where(c => !SqlCatalog.IsBinaryLike(c.BaseType))
                .Take(MaxSampledColumns)
                .Select(c => c.Name)
                .ToList();
            if (columns.Count == 0) continue;

            bool random = table.RowCount is >= 0 && table.RowCount <= options.RandomSampleMaxRows;
            string sql = SqlCatalog.SampleQuery(engine, table, columns, rowsWanted, random);

            try
            {
                var rows = new List<Dictionary<string, object?>>();
                await ReadRowsAsync(conn, sql, r =>
                {
                    var row = new Dictionary<string, object?>(r.FieldCount);
                    for (int i = 0; i < r.FieldCount; i++)
                    {
                        object? value;
                        try
                        {
                            value = r.IsDBNull(i) ? null : SampleDataSanitizer.Normalize(r.GetValue(i));
                        }
                        catch (Exception)
                        {
                            value = "<unreadable>";
                        }

                        row[r.GetName(i)] = options.MaskSampleData ? SampleDataSanitizer.Mask(r.GetName(i), value) : value;
                    }
                    rows.Add(row);
                }, ct, SampleCommandTimeoutSeconds).ConfigureAwait(false);

                if (rows.Count > 0) schema.SampleData[table.FullName] = rows;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                schema.Warnings.Add($"Sample rows of '{table.FullName}' could not be read: {FirstLine(ex.Message)}");
                if (engine == DbEngine.PostgreSql && conn.State != ConnectionState.Open) break;
            }
        }
    }

    // ------------------------------------------------------------------ helpers

    private static async Task ReadRowsAsync(
        DbConnection conn, string sql, Action<DbDataReader> onRow, CancellationToken ct, int timeoutSeconds = 60)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = timeoutSeconds;
        // Oracle returns defaults and trigger bodies as LONG; fetch them completely in the first round trip.
        if (cmd is OracleCommand oracle) oracle.InitialLONGFetchSize = -1;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            onRow(reader);
    }

    private static async Task ExecuteAsync(DbConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Optional sections must never abort the whole extraction (permissions, old server versions...).</summary>
    private static async Task TrySectionAsync(DatabaseSchema schema, string section, Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            schema.Warnings.Add($"{section} could not be read: {FirstLine(ex.Message)}");
        }
    }

    private static string Key(string? schema, string name) => (schema ?? "") + "\u001f" + name;

    private static string? CleanDefinition(string? definition, SchemaExtractionOptions options)
    {
        if (!options.IncludeDefinitions || string.IsNullOrWhiteSpace(definition)) return null;
        string trimmed = definition.Trim();
        return trimmed.Length <= options.MaxDefinitionChars
            ? trimmed
            : trimmed[..options.MaxDefinitionChars] + $"\n-- … truncated ({trimmed.Length - options.MaxDefinitionChars} more characters)";
    }

    private static string? Shorten(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max] + "…";

    private static string? NormalizeDefault(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Equals("NULL", StringComparison.OrdinalIgnoreCase)) return null; // MariaDB reports NULL as text
        return Shorten(value.Trim(), 120);
    }

    private static string? NormalizeAction(string? action)
    {
        if (string.IsNullOrWhiteSpace(action)) return null;
        string normalized = action.Replace('_', ' ').ToUpperInvariant();
        return normalized == "NO ACTION" ? null : normalized; // NO ACTION is the default, keep payload small
    }

    private static string FirstLine(string message)
    {
        int nl = message.IndexOfAny(new[] { '\r', '\n' });
        string line = nl >= 0 ? message[..nl] : message;
        return line.Length > 200 ? line[..200] + "…" : line;
    }
}

internal static class DbReaderExtensions
{
    public static string? Str(this DbDataReader r, int i) =>
        r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture);

    public static long? Long(this DbDataReader r, int i)
    {
        if (r.IsDBNull(i)) return null;
        object v = r.GetValue(i);
        return v switch
        {
            long l => l,
            int n => n,
            short s => s,
            decimal m => (long)m,
            double d => (long)d,
            float f => (long)f,
            ulong u => u > long.MaxValue ? long.MaxValue : (long)u,
            _ => long.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null
        };
    }

    public static bool Bool(this DbDataReader r, int i)
    {
        if (r.IsDBNull(i)) return false;
        object v = r.GetValue(i);
        return v switch
        {
            bool b => b,
            decimal m => m != 0, // Oracle NUMBER
            double d => d != 0,
            byte or sbyte or short or ushort or int or uint or long or ulong => Convert.ToInt64(v, CultureInfo.InvariantCulture) != 0,
            string s => s is "1" or "t" or "T" or "true" or "True" or "TRUE" or "YES" or "yes" or "Y",
            _ => false
        };
    }
}
