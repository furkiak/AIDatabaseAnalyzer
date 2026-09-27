using System.Text.Json;
using AIDatabaseAnalyzer.Services;
using Microsoft.Data.SqlClient;
using Xunit.Abstractions;

namespace AIDatabaseAnalyzer.ConnectionTests;

[Collection("SQL Server fixtures")]
public sealed class DelimiterSchemaExtractionTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task SqlServerSeparatelySelectsDotCollisionsAndPreservesEveryRelatedIdentity()
    {
        var server = Environment.GetEnvironmentVariable("AIDB_TEST_SQLSERVER")
            ?? throw new InvalidOperationException("Set AIDB_TEST_SQLSERVER to an isolated SQL Server test instance name.");
        var connectionString = DatabaseConnectionStrings.WithDatabase(DbEngine.SqlServer,
            DatabaseConnectionStrings.Build(DbEngine.SqlServer, server, null, "", "", allowUntrustedCertificate: true), "tempdb");
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var plain = "aidb_dot_" + suffix;
        var dotted = plain + ".b";
        var one = new DatabaseObjectIdentity(dotted, "c");
        var two = new DatabaseObjectIdentity(plain, "b.c");
        var parent = new DatabaseObjectIdentity(plain, "Parent");
        var leftView = new DatabaseObjectIdentity(dotted, "v");
        var rightView = new DatabaseObjectIdentity(plain, "b.v");
        var leftProcedure = new DatabaseObjectIdentity(dotted, "p");
        var rightProcedure = new DatabaseObjectIdentity(plain, "b.p");
        var trigger = "tr_" + suffix;
        var fkName = "FK_" + suffix;
        var indexName = "IX_" + suffix;
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        try
        {
            await Execute($"CREATE SCHEMA [{plain}]");
            await Execute($"CREATE SCHEMA [{dotted}]");
            await Execute($"CREATE TABLE {Quote(parent)} (PartA int NOT NULL, PartB int NOT NULL, CONSTRAINT [PK_{suffix}] PRIMARY KEY (PartA, PartB))");
            await Execute($"CREATE TABLE {Quote(one)} (LeftId int NOT NULL, Marker nvarchar(32) CONSTRAINT [DF_{suffix}] DEFAULT ('LEFT_DEFAULT'), RefA int NULL, RefB int NULL, CONSTRAINT [CK_{suffix}] CHECK (LeftId > 0), CONSTRAINT [{fkName}] FOREIGN KEY (RefA, RefB) REFERENCES {Quote(parent)}(PartA, PartB))");
            await Execute($"CREATE TABLE {Quote(two)} (RightId int NOT NULL, Marker nvarchar(32) CONSTRAINT [DF_{suffix}] DEFAULT ('RIGHT_DEFAULT'), RefA int NULL, RefB int NULL, CONSTRAINT [CK_{suffix}] CHECK (RightId > 0), CONSTRAINT [{fkName}] FOREIGN KEY (RefA, RefB) REFERENCES {Quote(parent)}(PartA, PartB))");
            await Execute($"CREATE INDEX [{indexName}] ON {Quote(one)}(RefB, RefA) INCLUDE (Marker) WHERE RefB IS NOT NULL");
            await Execute($"CREATE INDEX [{indexName}] ON {Quote(two)}(RefA, RefB) INCLUDE (Marker)");
            await Execute($"INSERT INTO {Quote(parent)} VALUES (1,2)");
            await Execute($"INSERT INTO {Quote(one)} VALUES (10,'LEFT_COLLISION',1,2)");
            await Execute($"INSERT INTO {Quote(two)} VALUES (20,'RIGHT_COLLISION',1,2)");
            await Execute($"CREATE TRIGGER [{dotted}].[{trigger}] ON {Quote(one)} AFTER INSERT AS BEGIN SET NOCOUNT ON; END");
            await Execute($"CREATE TRIGGER [{plain}].[{trigger}] ON {Quote(two)} AFTER INSERT AS BEGIN SET NOCOUNT ON; END");
            await Execute($"CREATE VIEW {Quote(leftView)} AS SELECT LeftId FROM {Quote(one)}");
            await Execute($"CREATE VIEW {Quote(rightView)} AS SELECT RightId FROM {Quote(two)}");
            await Execute($"CREATE PROCEDURE {Quote(leftProcedure)} @LeftParameter int AS SELECT @LeftParameter");
            await Execute($"CREATE PROCEDURE {Quote(rightProcedure)} @RightParameter nvarchar(20) AS SELECT @RightParameter");

            var service = new DbSchemaService();
            var identities = await service.GetTableIdentitiesAsync(DbEngine.SqlServer, connectionString);
            Assert.Single(identities, identity => identity == one);
            Assert.Single(identities, identity => identity == two);
            foreach (var (identity, marker, column) in new[]
            {
                (one, "LEFT_COLLISION", "LeftId"), (two, "RIGHT_COLLISION", "RightId")
            })
            {
                using var single = JsonDocument.Parse(await Extract([identity]));
                var root = single.RootElement;
                Assert.Single(root.GetProperty("Tables").EnumerateObject());
                Assert.Contains(column, root.GetProperty("Tables").GetProperty(identity.Key).EnumerateArray().Select(c => c.GetProperty("Column").GetString()));
                Assert.Equal(marker, root.GetProperty("SampleData").GetProperty(identity.Key)[0].GetProperty("Marker").GetString());
                CheckIdentity(root.GetProperty("TableIdentities").GetProperty(identity.Key), identity);
                Assert.All(root.GetProperty("Indexes").EnumerateArray(), index => CheckIdentity(index.GetProperty("TableIdentity"), identity));
                Assert.All(root.GetProperty("Constraints").EnumerateArray(), constraint => CheckIdentity(constraint.GetProperty("TableIdentity"), identity));
                var selectedTrigger = Assert.Single(root.GetProperty("Triggers").EnumerateArray());
                CheckIdentity(selectedTrigger.GetProperty("TableIdentity"), identity);
                CheckIdentity(selectedTrigger.GetProperty("Identity"), new DatabaseObjectIdentity(identity.Schema, trigger));
                CheckIdentity(Assert.Single(root.GetProperty("ForeignKeys").EnumerateArray()).GetProperty("FromIdentity"), identity);
            }

            var json = await Extract([one, two]);
            using var together = JsonDocument.Parse(json);
            var both = together.RootElement;
            Assert.Equal(2, both.GetProperty("Tables").EnumerateObject().Count());
            Assert.Equal(2, both.GetProperty("TableIdentities").EnumerateObject().Count());
            Assert.Equal(2, both.GetProperty("SampleData").EnumerateObject().Count());
            Assert.Equal(2, both.GetProperty("ForeignKeys").GetArrayLength());
            foreach (var fk in both.GetProperty("ForeignKeys").EnumerateArray())
            {
                Assert.Equal(fkName, fk.GetProperty("Name").GetString());
                CheckIdentity(fk.GetProperty("ToIdentity"), parent);
                Assert.Equal(parent.Key, fk.GetProperty("To").GetString());
                Assert.Equal(new[] { "RefA", "RefB" }, fk.GetProperty("Columns").EnumerateArray().Select(c => c.GetProperty("From").GetString()));
                Assert.Equal(new[] { "PartA", "PartB" }, fk.GetProperty("Columns").EnumerateArray().Select(c => c.GetProperty("To").GetString()));
                Assert.Equal(new[] { 1, 2 }, fk.GetProperty("Columns").EnumerateArray().Select(c => c.GetProperty("Ordinal").GetInt32()));
            }
            var indexes = both.GetProperty("Indexes").EnumerateArray().Where(index => index.GetProperty("Name").GetString() == indexName).ToArray();
            Assert.Equal(2, indexes.Length);
            var leftIndex = indexes.Single(index => index.GetProperty("Table").GetString() == one.Key);
            var rightIndex = indexes.Single(index => index.GetProperty("Table").GetString() == two.Key);
            Assert.Equal(new[] { "RefB", "RefA", "Marker" }, leftIndex.GetProperty("Columns").EnumerateArray().Select(c => c.GetProperty("Name").GetString()));
            Assert.Equal(new[] { "RefA", "RefB", "Marker" }, rightIndex.GetProperty("Columns").EnumerateArray().Select(c => c.GetProperty("Name").GetString()));
            Assert.True(leftIndex.GetProperty("Columns")[2].GetProperty("Included").GetBoolean());
            Assert.Contains("[RefB] IS NOT NULL", leftIndex.GetProperty("Filter").GetString());
            foreach (var identity in new[] { one, two })
            {
                var constraints = both.GetProperty("Constraints").EnumerateArray().Where(c => c.GetProperty("Table").GetString() == identity.Key).ToArray();
                Assert.Contains(constraints, c => c.GetProperty("Type").GetString() == "CHECK" && c.GetProperty("Definition").GetString()!.Contains(identity == one ? "LeftId" : "RightId"));
                Assert.Contains(constraints, c => c.GetProperty("Type").GetString() == "DEFAULT" && c.GetProperty("Definition").GetString()!.Contains(identity == one ? "LEFT_DEFAULT" : "RIGHT_DEFAULT"));
            }
            Assert.Contains(leftView.Key, both.GetProperty("Views").EnumerateArray().Select(view => view.GetString()));
            Assert.Contains(rightView.Key, both.GetProperty("Views").EnumerateArray().Select(view => view.GetString()));
            CheckIdentity(both.GetProperty("ViewIdentities").GetProperty(leftView.Key), leftView);
            CheckIdentity(both.GetProperty("ViewIdentities").GetProperty(rightView.Key), rightView);
            Assert.Equal("@LeftParameter", both.GetProperty("StoredProcedures").GetProperty(leftProcedure.Key)[0].GetString());
            Assert.Equal("@RightParameter", both.GetProperty("StoredProcedures").GetProperty(rightProcedure.Key)[0].GetString());
            CheckIdentity(both.GetProperty("RoutineIdentities").GetProperty(leftProcedure.Key), leftProcedure);
            CheckIdentity(both.GetProperty("RoutineIdentities").GetProperty(rightProcedure.Key), rightProcedure);
            var legacyKeys = await service.GetTablesAsync(DbEngine.SqlServer, connectionString);
            Assert.Contains(one.Key, legacyKeys);
            Assert.Contains(two.Key, legacyKeys);
            Assert.NotEqual(one.Key, two.Key);
            using var legacy = JsonDocument.Parse(await service.ExtractSchemaAsJsonAsync(DbEngine.SqlServer, connectionString,
                new SchemaFilterConfig { SelectedTables = [one.Key], IncludeSampleData = true }));
            Assert.Single(legacy.RootElement.GetProperty("Tables").EnumerateObject());
            await Assert.ThrowsAsync<ArgumentException>(() => service.ExtractSchemaAsJsonAsync(DbEngine.SqlServer, connectionString,
                new SchemaFilterConfig { SelectedTables = [dotted + ".c"] }));
            var safe = SchemaPrivacy.WithoutSamples(json);
            Assert.DoesNotContain("LEFT_COLLISION", safe);
            Assert.DoesNotContain("RIGHT_COLLISION", safe);
            var evidence = SchemaEvidenceSummary.Create(safe);
            Assert.Contains("TableIdentities", evidence);
            Assert.Contains($"TableIdentities[{JsonSerializer.Serialize(one.Key)}]", evidence);
            Assert.Contains($"TableIdentities[{JsonSerializer.Serialize(two.Key)}]", evidence);
            var diagram = SchemaDiagramHtml.Create(json);
            Assert.DoesNotContain("LEFT_COLLISION", diagram);
            Assert.Contains(JsonSerializer.Serialize(one.Key), diagram);
            Assert.Contains(JsonSerializer.Serialize(two.Key), diagram);
            output.WriteLine($"SQL Server {connection.ServerVersion}; tempdb owned fixture: separate typed selection/extraction/sampling of [a.b].[c] and [a].[b.c]; two same-named composite FKs and indexes kept distinct; check/default/trigger/view/routine identities and sample-free export/report paths passed.");

            Task<string> Extract(List<DatabaseObjectIdentity> selected) => service.ExtractSchemaAsJsonAsync(
                DbEngine.SqlServer, connectionString, new SchemaFilterConfig { SelectedTableIdentities = selected, IncludeSampleData = true });
        }
        finally
        {
            await Execute($"DROP PROCEDURE IF EXISTS {Quote(leftProcedure)}");
            await Execute($"DROP PROCEDURE IF EXISTS {Quote(rightProcedure)}");
            await Execute($"DROP VIEW IF EXISTS {Quote(leftView)}");
            await Execute($"DROP VIEW IF EXISTS {Quote(rightView)}");
            await Execute($"DROP TABLE IF EXISTS {Quote(one)}");
            await Execute($"DROP TABLE IF EXISTS {Quote(two)}");
            await Execute($"DROP TABLE IF EXISTS {Quote(parent)}");
            await Execute($"DROP SCHEMA IF EXISTS [{dotted}]");
            await Execute($"DROP SCHEMA IF EXISTS [{plain}]");
        }
        async Task Execute(string sql)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
    }

    private static string Quote(DatabaseObjectIdentity identity) => DbSchemaService.QuoteTable(DbEngine.SqlServer, identity.Schema, identity.Name);
    private static void CheckIdentity(JsonElement actual, DatabaseObjectIdentity expected)
    {
        Assert.Equal(expected.Schema, actual.GetProperty("Schema").GetString());
        Assert.Equal(expected.Name, actual.GetProperty("Name").GetString());
    }
}
