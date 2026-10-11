using System.Text.Json;
using AIDatabaseAnalyzer.Models;
using AIDatabaseAnalyzer.Services.Database;
using AIDatabaseAnalyzer.Services.History;
using AIDatabaseAnalyzer.Services.Insights;
using AIDatabaseAnalyzer.Services.Reporting;

namespace AIDatabaseAnalyzer.Tests;

public class SchemaLinkTests
{
    [Theory]
    [InlineData("customers", "customers")]
    [InlineData("CUSTOMERS.email", "customers")]
    [InlineData("orders(customer_id)", "orders")]
    [InlineData("\"customers\"", "customers")]
    [InlineData("[customers].[id]", "customers")]
    [InlineData("count(*)", null)]
    [InlineData("SELECT 1", null)]
    public void Written_names_resolve_to_tables(string token, string? expected) =>
        Assert.Equal(expected, SchemaLinks.ResolveTable(token, TestData.FlawedShop()));

    [Fact]
    public void Report_sections_are_mapped_to_the_tables_they_mention()
    {
        var schema = TestData.FlawedShop();
        var report = new AnalysisReport();
        report.Sections.Add(new ReportSection { Title = "A", Content = "Index `orders.customer_id` and check `customers`." });
        report.Sections.Add(new ReportSection { Title = "B", Content = "Nothing about tables, just `SELECT 1`." });
        report.Sections.Add(new ReportSection { Title = "C", Content = "`orders` again", IsError = true });

        var mentions = SchemaLinks.Mentions(report, schema);

        Assert.Equal(new[] { 0 }, mentions["orders"]);
        Assert.Equal(new[] { 0 }, mentions["customers"]);
        Assert.Equal(2, mentions.Count);
    }

    [Fact]
    public void Findings_know_their_tables_even_beyond_the_listed_objects()
    {
        var insights = SchemaInsightsService.Analyze(TestData.FlawedShop());
        var noPk = insights.Findings.Single(f => f.RuleId == "NO_PK");
        Assert.Contains("audit_log", noPk.Tables);
        var fkIndex = insights.Findings.Single(f => f.RuleId == "FK_NO_INDEX");
        Assert.All(fkIndex.Tables, t => Assert.NotNull(TestData.FlawedShop().FindTable(t)));
    }

    [Fact]
    public void Diagram_data_carries_findings_mentions_quoting_and_focus()
    {
        var schema = TestData.FlawedShop();
        var insights = SchemaInsightsService.Analyze(schema);
        var report = new AnalysisReport { Id = "r1" };
        report.Sections.Add(new ReportSection { Title = "Summary", Content = "Look at `orders`." });

        string json = DiagramHtmlBuilder.BuildDataJson(schema, true, new DiagramContext { Insights = insights, Report = report, FocusRule = "NO_PK" });
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(insights.Score, root.GetProperty("meta").GetProperty("score").GetInt32());
        Assert.Equal("NO_PK", root.GetProperty("init").GetProperty("rule").GetString());
        Assert.True(root.GetProperty("init").GetProperty("health").GetBoolean());
        Assert.Equal("r1", root.GetProperty("report").GetProperty("id").GetString());

        var orders = root.GetProperty("tables").EnumerateArray().Single(t => t.GetProperty("n").GetString() == "orders");
        Assert.Equal(0, orders.GetProperty("m")[0].GetInt32());
        Assert.True(orders.GetProperty("f").GetArrayLength() > 0);
        var rules = root.GetProperty("rules");
        Assert.All(orders.GetProperty("f").EnumerateArray(), i => Assert.InRange(i.GetInt32(), 0, rules.GetArrayLength() - 1));
    }

    [Theory]
    [InlineData(DbEngine.PostgreSql, "orders", "orders")]
    [InlineData(DbEngine.PostgreSql, "Orders", "\"Orders\"")]
    [InlineData(DbEngine.SqlServer, "order", "[order]")]
    [InlineData(DbEngine.Oracle, "ORDERS", "ORDERS")]
    [InlineData(DbEngine.Oracle, "orders", "\"orders\"")]
    [InlineData(DbEngine.MySql, "my table", "`my table`")]
    public void Generated_sql_quotes_only_when_needed(DbEngine engine, string identifier, string expected) =>
        Assert.Equal(expected, SqlCatalog.QuoteIfNeeded(engine, identifier));
}

public class FixScriptTests
{
    [Fact]
    public void Script_creates_missing_foreign_key_indexes_and_comments_out_risky_changes()
    {
        var schema = TestData.FlawedShop();
        var result = FixScriptBuilder.Build(schema, new DateTime(2026, 1, 1));
        var lines = result.Sql.Split('\n').Select(l => l.TrimEnd()).ToList();

        int expected = SchemaInsightsService.UnindexedForeignKeys(schema).Count();
        Assert.True(expected > 0);
        Assert.Equal(expected, result.Statements);
        Assert.Equal(expected, lines.Count(l => l.StartsWith("CREATE INDEX", StringComparison.Ordinal)));

        // nothing destructive runs without a human: every DROP and ALTER is a comment
        Assert.DoesNotContain(lines, l => l.StartsWith("DROP", StringComparison.Ordinal) || l.StartsWith("ALTER", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("-- ALTER TABLE audit_log", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(DbEngine.PostgreSql, "CREATE INDEX CONCURRENTLY IF NOT EXISTS")]
    [InlineData(DbEngine.SqlServer, "IF NOT EXISTS (SELECT 1 FROM sys.indexes")]
    [InlineData(DbEngine.Sqlite, "CREATE INDEX IF NOT EXISTS")]
    public void Index_statements_use_the_engines_idempotent_form(DbEngine engine, string expected)
    {
        var schema = TestData.FlawedShop();
        schema.Engine = engine;
        Assert.Contains(expected, FixScriptBuilder.Build(schema).Sql);
    }
}

public class SchemaDiffTests
{
    private static DatabaseSchema Changed()
    {
        var schema = TestData.FlawedShop();
        var orders = schema.FindTable("orders")!;
        orders.Columns.Add(new ColumnInfo { Name = "channel", DataType = "varchar(20)", BaseType = "varchar", MaxLength = 20, IsNullable = true });
        var email = schema.FindTable("customers")!.Columns.First(c => c.Name == "email");
        email.IsNullable = false;
        email.DataType = "varchar(100)";
        email.MaxLength = 100;
        schema.Tables.Remove(schema.FindTable("audit_log")!);
        schema.Tables.Add(new TableInfo { Name = "coupons", Columns = { new ColumnInfo { Name = "id", DataType = "int", BaseType = "int", IsPrimaryKey = true } } });
        schema.Indexes.Add(new IndexInfo { Table = "orders", Name = "ix_orders_channel", Columns = { "channel" } });
        return schema;
    }

    [Fact]
    public void Identical_schemas_have_no_drift_and_the_same_fingerprint()
    {
        var a = TestData.FlawedShop();
        var b = TestData.FlawedShop();
        b.Tables[0].RowCount = 123_456; // data growth is not a structural change

        Assert.True(SchemaDiff.Compare(a, b).IsEmpty);
        Assert.Equal(SchemaDiff.Fingerprint(a), SchemaDiff.Fingerprint(b));
    }

    [Fact]
    public void Added_removed_and_changed_objects_are_reported_with_breaking_changes()
    {
        var diff = SchemaDiff.Compare(TestData.FlawedShop(), Changed());

        Assert.Equal(new[] { "coupons" }, diff.AddedTables);
        Assert.Equal(new[] { "audit_log" }, diff.RemovedTables);
        Assert.Contains(diff.ChangedTables, t => t.Table == "orders" && t.AddedColumns.Single().StartsWith("channel"));
        var email = diff.ChangedTables.Single(t => t.Table == "customers").ChangedColumns.Single();
        Assert.True(email.Breaking); // NOT NULL added and shortened
        Assert.Single(diff.AddedIndexes);
        Assert.Equal(2, diff.BreakingChanges); // dropped table + narrowed column
        Assert.Contains("potentially breaking", diff.Summary);

        string md = diff.ToMarkdown("Monday", "Today");
        Assert.Contains("## New tables (1)", md);
        Assert.Contains("`coupons`", md);
        Assert.NotEqual(SchemaDiff.Fingerprint(TestData.FlawedShop()), SchemaDiff.Fingerprint(Changed()));
    }

    [Fact]
    public void Sections_missing_on_one_side_are_not_compared()
    {
        var before = TestData.FlawedShop();
        var after = TestData.FlawedShop();
        after.IndexesIncluded = false;
        after.Indexes.Clear();

        var diff = SchemaDiff.Compare(before, after);
        Assert.Empty(diff.RemovedIndexes);
        Assert.Contains("indexes", diff.NotCompared);
    }
}

public sealed class SnapshotStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "aida-snapshots-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }

    [Fact]
    public async Task Snapshots_round_trip_without_sample_rows_and_skip_unchanged_versions()
    {
        var store = new SnapshotStore(_folder);
        var schema = TestData.FlawedShop();
        schema.SampleData["customers"] = new List<Dictionary<string, object?>> { new() { ["email"] = "x" } };
        var insights = SchemaInsightsService.Analyze(schema);

        var first = await store.SaveAsync(schema, "Server", insights);
        var again = await store.SaveAsync(schema, "server ", insights);
        Assert.Equal(first.Id, again.Id); // same structure → no duplicate

        var loaded = await store.LoadAsync(first.Id);
        Assert.NotNull(loaded);
        Assert.Empty(loaded!.SampleData);
        Assert.Equal(schema.Tables.Count, loaded.Tables.Count);
        Assert.Equal(schema.ForeignKeys.Count, loaded.ForeignKeys.Count);
        Assert.True(SchemaDiff.Compare(schema, loaded).IsEmpty);
        Assert.Equal(SchemaDiff.Fingerprint(schema), SchemaDiff.Fingerprint(loaded));
        Assert.Single(schema.SampleData); // the live schema keeps its samples

        schema.Tables.Add(new TableInfo { Name = "new_table" });
        await Task.Delay(5);
        var second = await store.SaveAsync(schema, "server", insights);
        Assert.NotEqual(first.Id, second.Id);

        var list = await store.ListAsync();
        Assert.Equal(new[] { second.Id, first.Id }, list.Select(s => s.Id));
        Assert.Equal(second.Id, (await store.LatestAsync(schema.Engine, "SERVER", schema.DatabaseName))!.Id);
        Assert.Equal(insights.Score, list[0].HealthScore);

        await store.DeleteAsync(second.Id);
        Assert.Single(await store.ListAsync());
    }
}

public class PageRenderingTests
{
    private static Func<string, string?> Linker(DatabaseSchema schema) =>
        token => SchemaLinks.ResolveTable(token, schema) is { } table ? "aida://table/" + Uri.EscapeDataString(table) : null;

    [Fact]
    public void Chat_page_escapes_questions_and_links_tables_in_answers()
    {
        var schema = TestData.FlawedShop();
        var entries = new[]
        {
            new ChatEntry(AIDatabaseAnalyzer.Services.Llm.LlmRole.User, "<script>alert(1)</script> about orders"),
            new ChatEntry(AIDatabaseAnalyzer.Services.Llm.LlmRole.Assistant, "Index `orders.customer_id`:\n\n```sql\nCREATE INDEX ix ON orders (customer_id);\n```")
        };

        string html = ChatRenderer.ToHtml(entries, "dark", Linker(schema), thinking: true, "Thinking…");

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("aida://table/orders", html);
        Assert.Contains("CREATE INDEX ix ON orders", html);
        Assert.Contains("class=\"bubble typing\"", html);
        Assert.Contains("data-theme=\"dark\"", html);
    }

    [Fact]
    public void Exported_reports_have_no_app_links_but_the_in_app_viewer_does()
    {
        var schema = TestData.FlawedShop();
        var report = new AnalysisReport { DatabaseName = "shop" };
        report.Findings.Add(new ReportFinding { Severity = "High", Title = "No PK", Objects = { "audit_log" } });
        report.Sections.Add(new ReportSection { Title = "S", Content = "See `orders`.\n\n```sql\nSELECT 1;\n```" });

        string exported = ReportRenderer.ToHtml(report);
        string inApp = ReportRenderer.ToHtml(report, "auto", Linker(schema), scrollToSection: 0);

        Assert.DoesNotContain("aida://", exported);
        Assert.Contains("href=\"aida://table/orders\"", inApp);
        Assert.Contains("href=\"aida://table/audit_log\"", inApp);
        Assert.Contains("getElementById('s1')", inApp);
    }

    [Fact]
    public void Chat_system_prompt_carries_the_schema_and_the_dialect()
    {
        var schema = TestData.FlawedShop();
        schema.Engine = DbEngine.Oracle;
        string prompt = AIDatabaseAnalyzer.Services.Llm.PromptBuilder.ChatSystemPrompt(schema, SchemaSerializer.ToJson(schema, false), SchemaInsightsService.Analyze(schema));

        Assert.Contains("<schema>", prompt);
        Assert.Contains("Oracle (SQL and PL/SQL)", prompt);
        Assert.Contains("Automated checks", prompt);
        Assert.Contains("language of the user's question", prompt);
    }

    [Fact]
    public void Markdown_pages_render_lists_and_links()
    {
        string html = MarkdownPage.ToHtml("## Changed\n- `customers` grew\n- <b>raw</b>", "light", Linker(TestData.FlawedShop()));
        Assert.Contains("<h2>Changed</h2>", html);
        Assert.Contains("aida://table/customers", html);
        Assert.DoesNotContain("<b>raw</b>", html);
    }
}
