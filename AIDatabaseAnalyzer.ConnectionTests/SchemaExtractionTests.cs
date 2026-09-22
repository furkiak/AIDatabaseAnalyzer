using System.Text.Json;
using AIDatabaseAnalyzer.Services;
using Microsoft.Data.SqlClient;

namespace AIDatabaseAnalyzer.ConnectionTests;

public class SchemaExtractionTests
{
    [Theory]
    [InlineData(DbEngine.SqlServer, "schema]x", "item]s", "[schema]]x].[item]]s]")]
    [InlineData(DbEngine.PostgreSql, "schema\"x", "item\"s", "\"schema\"\"x\".\"item\"\"s\"")]
    [InlineData(DbEngine.MySql, "schema`x", "item`s", "`schema``x`.`item``s`")]
    public void QuotesProviderIdentifiers(DbEngine engine, string schema, string table, string expected) =>
        Assert.Equal(expected, DbSchemaService.QuoteTable(engine, schema, table));

    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task SqlServerKeepsSameNamedTablesInDifferentSchemasSeparate()
    {
        var server = Environment.GetEnvironmentVariable("AIDB_TEST_SQLSERVER")
            ?? throw new InvalidOperationException("Set AIDB_TEST_SQLSERVER to an isolated SQL Server test instance.");
        var connString = DatabaseConnectionStrings.WithDatabase(DbEngine.SqlServer,
            DatabaseConnectionStrings.Build(DbEngine.SqlServer, server, null, "", "", allowUntrustedCertificate: true), "tempdb");
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var one = "aidb_a_" + suffix;
        var two = "aidb_b_" + suffix;
        await using var conn = new SqlConnection(connString);
        await conn.OpenAsync();
        try
        {
            await Execute($"CREATE SCHEMA [{one}]");
            await Execute($"CREATE SCHEMA [{two}]");
            await Execute($"CREATE TABLE [{one}].[Parent] (PartA int NOT NULL, PartB int NOT NULL, CONSTRAINT [PK_{suffix}] PRIMARY KEY (PartA, PartB))");
            await Execute($"CREATE TABLE [{one}].[Items] (Id int, Marker nvarchar(32), PartA int NULL, PartB int NULL, CONSTRAINT [CK_{suffix}] CHECK (Id > 0), CONSTRAINT [FK_{suffix}] FOREIGN KEY (PartA, PartB) REFERENCES [{one}].[Parent](PartA, PartB))");
            await Execute($"CREATE TABLE [{two}].[Items] (Id int, Marker nvarchar(32))");
            await Execute($"CREATE TABLE [{one}].[Odd]]Name] (Id int, Marker nvarchar(32))");
            await Execute($"CREATE INDEX [IX_{suffix}] ON [{one}].[Items](PartB, PartA) INCLUDE (Marker) WHERE PartB IS NOT NULL");
            await Execute($"INSERT INTO [{one}].[Items](Id, Marker) VALUES (1, 'FIRST_SCHEMA')");
            await Execute($"INSERT INTO [{two}].[Items] VALUES (2, 'SECOND_SCHEMA')");
            await Execute($"INSERT INTO [{one}].[Odd]]Name] VALUES (3, 'ESCAPED_NAME')");

            var service = new DbSchemaService();
            var tables = await service.GetTablesAsync(DbEngine.SqlServer, connString);
            Assert.Contains($"{one}.Items", tables);
            Assert.Contains($"{two}.Items", tables);
            Assert.Contains($"{one}.Odd]Name", tables);

            var json = await service.ExtractSchemaAsJsonAsync(DbEngine.SqlServer, connString,
                new SchemaFilterConfig
                {
                    SelectedTables = new List<string> { $"{one}.Items", $"{one}.Parent", $"{two}.Items", $"{one}.Odd]Name", $"{one}.Missing" },
                    IncludeSampleData = true,
                    IncludeViews = false,
                    IncludeSps = false,
                    IncludeTriggers = false,
                    IncludeIndexes = true,
                    IncludeConstraints = true
                });
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            Assert.True(root.GetProperty("Tables").TryGetProperty($"{one}.Items", out _));
            Assert.True(root.GetProperty("Tables").TryGetProperty($"{two}.Items", out _));
            Assert.Equal("FIRST_SCHEMA", root.GetProperty("SampleData").GetProperty($"{one}.Items")[0].GetProperty("Marker").GetString());
            Assert.Equal("SECOND_SCHEMA", root.GetProperty("SampleData").GetProperty($"{two}.Items")[0].GetProperty("Marker").GetString());
            Assert.Equal("ESCAPED_NAME", root.GetProperty("SampleData").GetProperty($"{one}.Odd]Name")[0].GetProperty("Marker").GetString());
            Assert.Contains($"{one}.Missing", root.GetProperty("SampleErrors")[0].GetString());
            var fk = root.GetProperty("ForeignKeys").EnumerateArray().Single(f => f.GetProperty("Name").GetString() == $"FK_{suffix}");
            Assert.Equal($"{one}.Parent", fk.GetProperty("To").GetString());
            Assert.Equal(new[] { "PartA", "PartB" }, fk.GetProperty("Columns").EnumerateArray().Select(c => c.GetProperty("From").GetString()));
            var index = root.GetProperty("Indexes").EnumerateArray().Single(i => i.GetProperty("Name").GetString() == $"IX_{suffix}");
            Assert.Equal(new[] { "PartB", "PartA", "Marker" }, index.GetProperty("Columns").EnumerateArray().Select(c => c.GetProperty("Name").GetString()));
            Assert.True(index.GetProperty("Columns")[2].GetProperty("Included").GetBoolean());
            Assert.Contains("[PartB] IS NOT NULL", index.GetProperty("Filter").GetString());
            Assert.Contains($"CK_{suffix}", root.GetProperty("Constraints").EnumerateArray().Select(c => c.GetProperty("Name").GetString()));
            Assert.True(root.GetProperty("Tables").GetProperty($"{one}.Parent")[0].GetProperty("IsPrimaryKey").GetBoolean());
        }
        finally
        {
            await Execute($"DROP TABLE IF EXISTS [{one}].[Items]");
            await Execute($"DROP TABLE IF EXISTS [{two}].[Items]");
            await Execute($"DROP TABLE IF EXISTS [{one}].[Odd]]Name]");
            await Execute($"DROP TABLE IF EXISTS [{one}].[Parent]");
            await Execute($"DROP SCHEMA IF EXISTS [{one}]");
            await Execute($"DROP SCHEMA IF EXISTS [{two}]");
        }

        async Task Execute(string sql)
        {
            await using var command = conn.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
    }
}
