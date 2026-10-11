using AIDatabaseAnalyzer.Models;
using AIDatabaseAnalyzer.Services.Database;
using AIDatabaseAnalyzer.Services.Insights;
using AIDatabaseAnalyzer.Services.Llm;
using Microsoft.Data.Sqlite;

namespace AIDatabaseAnalyzer.Tests;

/// <summary>End-to-end extraction from a real SQLite file (runs everywhere, no server needed).</summary>
public sealed class SqliteExtractionTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"aida-{Guid.NewGuid():N}.sqlite");

    public SqliteExtractionTests()
    {
        using var conn = new SqliteConnection($"Data Source={_file};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE customers (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              email VARCHAR(255) NOT NULL,
              password TEXT,
              created_at TEXT DEFAULT CURRENT_TIMESTAMP,
              CONSTRAINT ck_email CHECK (email LIKE '%@%')
            );
            CREATE TABLE products (sku TEXT PRIMARY KEY, price REAL CHECK (price >= 0)) WITHOUT ROWID;
            CREATE TABLE orders (id INTEGER PRIMARY KEY, customer_id INTEGER NOT NULL REFERENCES customers ON DELETE CASCADE, total NUMERIC(10,2));
            CREATE TABLE order_lines (
              order_id INTEGER NOT NULL REFERENCES orders(id), line_no INTEGER NOT NULL, sku TEXT REFERENCES products(sku),
              PRIMARY KEY (order_id, line_no));
            CREATE TABLE audit_log (event TEXT);
            CREATE INDEX ix_orders_total ON orders(total) WHERE total > 0;
            CREATE VIEW v_orders AS SELECT * FROM orders;
            CREATE TRIGGER trg_audit AFTER DELETE ON orders BEGIN INSERT INTO audit_log VALUES ('deleted'); END;
            INSERT INTO customers(email, password) VALUES ('ann@example.com', 'hunter2'), ('bob@example.com', 'secret');
            INSERT INTO orders(customer_id, total) VALUES (1, 10.5), (2, 20);
            """;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_file); } catch (IOException) { }
    }

    private ConnectionSettings Connection => new() { Engine = DbEngine.Sqlite, Server = _file };

    [Fact]
    public async Task The_file_is_one_database_named_after_the_file()
    {
        var databases = await new DbSchemaService().GetDatabasesAsync(Connection);
        Assert.Equal(new[] { Path.GetFileNameWithoutExtension(_file) }, databases);
    }

    [Fact]
    public async Task Keys_relationships_checks_and_code_are_extracted()
    {
        var schema = await new DbSchemaService().ExtractSchemaAsync(Connection, "shop",
            new SchemaExtractionOptions { IncludeSampleData = true, IncludeDefinitions = true });

        Assert.Empty(schema.Warnings);
        Assert.Equal(5, schema.Tables.Count);
        Assert.Equal(2, schema.FindTable("customers")!.RowCount);

        // rowid alias, composite and WITHOUT ROWID primary keys
        Assert.True(schema.FindTable("customers")!.Columns.Single(c => c.Name == "id") is { IsPrimaryKey: true, IsIdentity: true, IsNullable: false });
        Assert.Equal(new[] { "order_id", "line_no" }, schema.FindTable("order_lines")!.PrimaryKeyColumns.Select(c => c.Name));
        Assert.True(schema.FindTable("products")!.HasPrimaryKey);
        Assert.False(schema.FindTable("audit_log")!.HasPrimaryKey);

        Assert.Equal(255, schema.FindTable("customers")!.Columns.Single(c => c.Name == "email").MaxLength);
        Assert.Null(schema.FindTable("orders")!.Columns.Single(c => c.Name == "total").MaxLength);

        // a reference without a column list points at the parent's primary key
        var fk = schema.ForeignKeys.Single(f => f.FromTable == "orders");
        Assert.Equal("customers", fk.ToTable);
        Assert.Equal(new[] { "id" }, fk.ToColumns);
        Assert.Equal("CASCADE", fk.OnDelete);
        Assert.Equal(3, schema.ForeignKeys.Count);

        Assert.Contains(schema.Indexes, i => i.Name == "ix_orders_total" && i.Filter == "total > 0");
        Assert.Contains(schema.Constraints, c => c.Name == "ck_email" && c.Definition == "email LIKE '%@%'");
        Assert.Contains(schema.Constraints, c => c.Table == "products" && c.Definition == "price >= 0");
        Assert.Contains(schema.Triggers, t => t.Name == "trg_audit" && t.Events == "DELETE" && t.Timing == "AFTER");
        Assert.Contains(schema.Views, v => v.Name == "v_orders" && v.Definition!.Contains("SELECT"));

        // sample rows are masked
        var row = schema.SampleData["customers"][0];
        Assert.DoesNotContain("@example.com", row["email"]?.ToString());
        Assert.StartsWith("<redacted", row["password"]?.ToString());
    }

    [Fact]
    public async Task Rules_understand_sqlite_types()
    {
        var schema = await new DbSchemaService().ExtractSchemaAsync(Connection, "shop", new SchemaExtractionOptions());
        var insights = SchemaInsightsService.Analyze(schema);

        Assert.Contains(insights.Findings, f => f.RuleId == "NO_PK" && f.Objects.Contains("audit_log"));
        Assert.Contains(insights.Findings, f => f.RuleId == "MONEY_AS_FLOAT");
        Assert.DoesNotContain(insights.Findings, f => f.RuleId == "DATE_AS_TEXT"); // ISO text is the SQLite way
    }

    [Fact]
    public async Task The_file_is_opened_read_only()
    {
        SqliteConnection.ClearAllPools();
        byte[] before = await File.ReadAllBytesAsync(_file);
        DateTime written = File.GetLastWriteTimeUtc(_file);

        await new DbSchemaService().ExtractSchemaAsync(Connection, "shop", new SchemaExtractionOptions { IncludeSampleData = true });

        Assert.Equal(before, await File.ReadAllBytesAsync(_file));
        Assert.Equal(written, File.GetLastWriteTimeUtc(_file));
        Assert.Contains("Mode=ReadOnly", DbSchemaService.BuildConnectionString(Connection, null));
    }

    [Fact]
    public async Task Missing_files_give_a_clear_error()
    {
        var ex = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            new DbSchemaService().GetDatabasesAsync(new ConnectionSettings { Engine = DbEngine.Sqlite, Server = "/no/such/file.db" }));
        Assert.Contains("does not exist", ex.Message);
    }
}

public class EngineCatalogTests
{
    [Theory]
    [InlineData("CREATE TABLE t (a INT CHECK (a > 0), b TEXT, CONSTRAINT ck_b CHECK (length(b) < 10 AND b <> ')'))", "check_1:a > 0|ck_b:length(b) < 10 AND b <> ')'")]
    [InlineData("CREATE TABLE t (checked INT, recheck TEXT)", "")]
    public void Sqlite_check_constraints_are_parsed_from_create_table(string sql, string expected)
    {
        var checks = SqlCatalog.ParseSqliteChecks(sql);
        Assert.Equal(expected, string.Join("|", checks.Select(c => $"{c.Name}:{c.Expression}")));
    }

    [Fact]
    public void Oracle_connection_uses_ezconnect_unless_a_full_address_is_given()
    {
        var settings = new ConnectionSettings { Engine = DbEngine.Oracle, Server = "db.local", ServiceName = "ORCLPDB1", Username = "app", Password = "pw" };
        Assert.Equal("db.local:1521/ORCLPDB1", DbSchemaService.OracleDataSource(settings));

        settings.Server = "tcps://adb.eu-frankfurt-1.oraclecloud.com:1522/abc_high.adb.oraclecloud.com";
        Assert.Equal(settings.Server, DbSchemaService.OracleDataSource(settings));
        Assert.Contains("User Id=app", DbSchemaService.BuildConnectionString(settings, null), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(DbEngine.Oracle, "SELECT \"a\" FROM \"T\" ORDER BY DBMS_RANDOM.VALUE FETCH FIRST 3 ROWS ONLY")]
    [InlineData(DbEngine.Sqlite, "SELECT \"a\" FROM \"T\" ORDER BY random() LIMIT 3")]
    public void Sample_queries_use_each_engines_syntax(DbEngine engine, string expected) =>
        Assert.Equal(expected, SqlCatalog.SampleQuery(engine, new TableInfo { Name = "T" }, new[] { "a" }, 3, random: true));

    [Fact]
    public void Every_engine_has_catalog_queries()
    {
        foreach (var engine in DbEngineExtensions.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(SqlCatalog.Tables(engine)));
            Assert.False(string.IsNullOrWhiteSpace(SqlCatalog.Columns(engine)));
            Assert.False(string.IsNullOrWhiteSpace(SqlCatalog.Indexes(engine)));
            Assert.False(string.IsNullOrWhiteSpace(SqlCatalog.ForeignKeys(engine)));
            Assert.False(string.IsNullOrWhiteSpace(engine.CompatibleServices()));
        }
        Assert.Contains("CURRENT_SCHEMA", SqlCatalog.Columns(DbEngine.Oracle));
        Assert.Contains("pragma_table_info", SqlCatalog.Columns(DbEngine.Sqlite));
    }
}

public class TopicCatalogTests
{
    [Fact]
    public void Titles_are_unique_and_every_topic_has_a_known_category()
    {
        var topics = TopicCatalog.CreateDefaults();
        Assert.Equal(topics.Count, topics.Select(t => t.Title).Distinct().Count());
        Assert.All(topics, t => Assert.Contains(t.Category, TopicCatalog.Categories));
        Assert.All(TopicCatalog.Categories, c => Assert.Contains(topics, t => t.Category == c));
    }

    [Fact]
    public void Presets_select_existing_topics()
    {
        var topics = TopicCatalog.CreateDefaults();
        foreach (var preset in TopicCatalog.Presets)
            Assert.True(topics.Count(preset.Includes) >= 4, preset.Name);
        Assert.Equal(8, topics.Count(TopicCatalog.Presets[0].Includes));
        Assert.Equal(topics.Count, topics.Count(TopicCatalog.Presets[^1].Includes));
    }

    [Fact]
    public void Limitations_explain_what_is_missing()
    {
        var schema = TestData.FlawedShop();
        schema.SampleData.Clear();
        Assert.Contains("sample rows", TopicCatalog.Limitation(TopicNeeds.SampleData, schema));
        Assert.Null(TopicCatalog.Limitation(TopicNeeds.None, schema));

        schema.Routines.Clear();
        Assert.NotNull(TopicCatalog.Limitation(TopicNeeds.Routines | TopicNeeds.CodeDefinitions, schema));
    }
}
