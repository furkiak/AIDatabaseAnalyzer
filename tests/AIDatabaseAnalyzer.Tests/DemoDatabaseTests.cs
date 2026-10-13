using AIDatabaseAnalyzer.Models;
using AIDatabaseAnalyzer.Services.Database;
using AIDatabaseAnalyzer.Services.Insights;
using Microsoft.Data.Sqlite;

namespace AIDatabaseAnalyzer.Tests;

/// <summary>The bundled demo database: it must build, read like a real shop and give the insights plenty to find.</summary>
public sealed class DemoDatabaseTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"aida-demo-{Guid.NewGuid():N}");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static ConnectionSettings Connection(string path) => new() { Engine = DbEngine.Sqlite, Server = path };

    [Fact]
    public async Task The_demo_builds_and_extracts_without_warnings()
    {
        string path = await DemoDatabase.EnsureAsync(_folder);
        var schema = await new DbSchemaService().ExtractSchemaAsync(Connection(path), "demo-shop",
            new SchemaExtractionOptions { IncludeSampleData = true, IncludeDefinitions = true });

        Assert.Empty(schema.Warnings);
        Assert.Equal(12, schema.Tables.Count);
        Assert.Equal(80, schema.FindTable("customers")!.RowCount);
        Assert.Equal(320, schema.FindTable("orders")!.RowCount);
        Assert.Equal(0, schema.FindTable("wishlists")!.RowCount);
        Assert.Contains(schema.Views, v => v.Name == "v_order_totals");
        Assert.Contains(schema.Triggers, t => t.Name == "trg_orders_cancelled");

        // sample rows leave the database masked
        var customer = schema.SampleData["customers"][0];
        Assert.DoesNotContain("@example.com", customer["email"]?.ToString());
        Assert.StartsWith("<redacted", customer["password"]?.ToString());
    }

    [Fact]
    public async Task The_demo_shows_the_typical_design_problems()
    {
        string path = await DemoDatabase.EnsureAsync(_folder);
        var schema = await new DbSchemaService().ExtractSchemaAsync(Connection(path), "demo-shop", new SchemaExtractionOptions());
        var rules = SchemaInsightsService.Analyze(schema).Findings.Select(f => f.RuleId).ToHashSet();

        foreach (var expected in new[] { "NO_PK", "PLAINTEXT_PASSWORD", "PII_COLUMNS", "MONEY_AS_FLOAT", "MISSING_FK", "FK_NO_INDEX", "DUPLICATE_INDEXES", "ISOLATED_TABLES", "EMPTY_TABLES" })
            Assert.True(rules.Contains(expected), $"{expected} was not found; got {string.Join(", ", rules)}");
    }

    [Fact]
    public async Task An_up_to_date_copy_is_reused_and_a_damaged_one_is_rebuilt()
    {
        string path = await DemoDatabase.EnsureAsync(_folder);
        DateTime first = File.GetLastWriteTimeUtc(path);

        await Task.Delay(50);
        Assert.Equal(path, await DemoDatabase.EnsureAsync(_folder));
        Assert.Equal(first, File.GetLastWriteTimeUtc(path));

        SqliteConnection.ClearAllPools();
        await File.WriteAllTextAsync(path, "not a database");
        await DemoDatabase.EnsureAsync(_folder);
        var databases = await new DbSchemaService().GetDatabasesAsync(Connection(path));
        Assert.Equal(new[] { "demo-shop" }, databases);
    }
}
