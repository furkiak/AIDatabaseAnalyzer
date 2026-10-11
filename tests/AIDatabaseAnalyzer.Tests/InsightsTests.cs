using AIDatabaseAnalyzer.Models;
using AIDatabaseAnalyzer.Services.Insights;

namespace AIDatabaseAnalyzer.Tests;

public class InsightsTests
{
    [Fact]
    public void Detects_the_deliberate_flaws_of_the_sample_schema()
    {
        var result = SchemaInsightsService.Analyze(TestData.FlawedShop());
        var ids = result.Findings.Select(f => f.RuleId).ToHashSet();

        Assert.Contains("NO_PK", ids);
        Assert.Contains("FK_TYPE_MISMATCH", ids);
        Assert.Contains("FK_NO_INDEX", ids);
        Assert.Contains("DUPLICATE_INDEXES", ids);
        Assert.Contains("MONEY_AS_FLOAT", ids);
        Assert.Contains("DATE_AS_TEXT", ids);
        Assert.Contains("PLAINTEXT_PASSWORD", ids);
        Assert.Contains("PII_COLUMNS", ids);
        Assert.InRange(result.Score, 0, 99);
    }

    [Fact]
    public void A_clean_schema_scores_one_hundred()
    {
        var schema = new DatabaseSchema { DatabaseName = "clean", Engine = DbEngine.PostgreSql };
        var t = new TableInfo { Name = "things", RowCount = 5, DefaultSchema = "public" };
        t.Columns.Add(TestData.Col("id", "integer", "int4", pk: true, identity: true));
        t.Columns.Add(TestData.Col("created_at", "timestamptz", "timestamptz"));
        t.Columns.Add(TestData.Col("title", "varchar(50)", "varchar", max: 50));
        t.Columns.Add(TestData.Col("amount", "numeric(12,2)", "numeric"));
        schema.Tables.Add(t);

        var pk = new IndexInfo { Table = "things", Name = "things_pkey", IsPrimary = true, IsUnique = true };
        pk.Columns.Add("id");
        schema.Indexes.Add(pk);

        var result = SchemaInsightsService.Analyze(schema);
        Assert.Equal(100, result.Score);
        Assert.Equal("A", result.Grade);
        Assert.DoesNotContain(result.Findings, f => f.Severity >= FindingSeverity.Low);
    }

    [Fact]
    public void Index_rules_are_skipped_when_indexes_were_not_extracted()
    {
        var schema = TestData.FlawedShop();
        schema.IndexesIncluded = false;
        schema.Indexes.Clear();

        var result = SchemaInsightsService.Analyze(schema);
        Assert.DoesNotContain(result.Findings, f => f.RuleId is "FK_NO_INDEX" or "DUPLICATE_INDEXES");
        Assert.NotEmpty(result.SkippedChecks);
    }

    [Fact]
    public void Findings_are_sorted_by_severity()
    {
        var result = SchemaInsightsService.Analyze(TestData.FlawedShop());
        var severities = result.Findings.Select(f => (int)f.Severity).ToList();
        Assert.Equal(severities.OrderByDescending(s => s), severities);
    }

    [Fact]
    public void Probable_missing_foreign_keys_are_found()
    {
        var schema = new DatabaseSchema { DatabaseName = "x", Engine = DbEngine.MySql };
        var users = new TableInfo { Name = "users" };
        users.Columns.Add(TestData.Col("id", "int", "int", pk: true));
        var posts = new TableInfo { Name = "posts" };
        posts.Columns.Add(TestData.Col("id", "int", "int", pk: true));
        posts.Columns.Add(TestData.Col("user_id", "int", "int"));
        schema.Tables.AddRange(new[] { users, posts });

        var finding = Assert.Single(SchemaInsightsService.Analyze(schema).Findings, f => f.RuleId == "MISSING_FK");
        Assert.Contains("posts.user_id → users", finding.Objects);
    }
}
