using System.Text.Json;
using AIDatabaseAnalyzer.Models;
using AIDatabaseAnalyzer.Services.Database;
using AIDatabaseAnalyzer.Services.Reporting;

namespace AIDatabaseAnalyzer.Tests;

public class SerializerTests
{
    [Fact]
    public void Compact_json_is_valid_and_describes_columns_in_one_string()
    {
        var schema = TestData.FlawedShop();
        schema.SampleData["customers"] = new() { new() { ["id"] = 1L, ["email"] = "a***@b***.com", ["note"] = null } };

        string json = SchemaSerializer.ToJson(schema, indented: false);
        using var doc = JsonDocument.Parse(json);

        var columns = doc.RootElement.GetProperty("Tables").GetProperty("customers").GetProperty("Columns");
        Assert.Equal("id integer PK IDENTITY NOT NULL", columns[0].GetString());
        Assert.Equal("PostgreSQL", doc.RootElement.GetProperty("Engine").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("SampleData").GetProperty("customers")[0].GetProperty("id").GetInt64());
        Assert.DoesNotContain("\n", json);
    }

    [Fact]
    public void Compact_json_needs_fewer_tokens_than_the_indented_form()
    {
        var schema = TestData.FlawedShop();
        Assert.True(SchemaSerializer.EstimateTokens(SchemaSerializer.ToJson(schema, false)) < SchemaSerializer.EstimateTokens(SchemaSerializer.ToJson(schema, true)));
    }

    [Fact]
    public void Data_dictionary_lists_tables_and_relationships()
    {
        string md = SchemaSerializer.ToMarkdownDictionary(TestData.FlawedShop());
        Assert.Contains("## orders", md);
        Assert.Contains("FK → customers.id", md);
    }

    [Fact]
    public void Diagram_data_cannot_break_out_of_its_script_tag()
    {
        var schema = TestData.FlawedShop();
        schema.Tables[0].Columns[1].Name = "</script><script>alert(1)</script>";

        string html = DiagramHtmlBuilder.Build(schema);
        Assert.DoesNotContain("</script><script>alert", html);
        Assert.Contains("\"rels\"", html);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "fk_orders_customer"));
    }
}

public class CatalogTests
{
    [Theory]
    [InlineData(DbEngine.SqlServer, "a]b", "[a]]b]")]
    [InlineData(DbEngine.PostgreSql, "a\"b", "\"a\"\"b\"")]
    [InlineData(DbEngine.MySql, "a`b", "`a``b`")]
    public void Identifiers_are_quoted_per_engine(DbEngine engine, string name, string expected) =>
        Assert.Equal(expected, SqlCatalog.Quote(engine, name));

    [Fact]
    public void Large_tables_are_not_sampled_with_a_full_scan_sort()
    {
        var table = new TableInfo { Schema = "dbo", Name = "Big" };
        string random = SqlCatalog.SampleQuery(DbEngine.SqlServer, table, new[] { "Id" }, 3, random: true);
        string plain = SqlCatalog.SampleQuery(DbEngine.SqlServer, table, new[] { "Id" }, 3, random: false);

        Assert.Contains("ORDER BY NEWID()", random);
        Assert.DoesNotContain("ORDER BY", plain);
        Assert.StartsWith("SELECT TOP (3)", plain);
    }

    [Fact]
    public void Connection_strings_cannot_be_injected_through_the_password()
    {
        var settings = new ConnectionSettings { Engine = DbEngine.SqlServer, Server = "srv", Username = "u", Password = "x;Trusted_Connection=True" };
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(DbSchemaService.BuildConnectionString(settings, "db"));

        Assert.Equal("x;Trusted_Connection=True", builder.Password);
        Assert.False(builder.IntegratedSecurity);
        Assert.Equal("db", builder.InitialCatalog);
    }

    [Fact]
    public void Table_names_hide_the_default_schema_only()
    {
        Assert.Equal("orders", TableInfo.Qualify("dbo", "orders", "dbo"));
        Assert.Equal("sales.orders", TableInfo.Qualify("sales", "orders", "dbo"));
        Assert.Equal("orders", TableInfo.Qualify(null, "orders", null));
    }
}
