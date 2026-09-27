using System.Text.Json;
using AIDatabaseAnalyzer.Services;

namespace AIDatabaseAnalyzer.ConnectionTests;

public sealed class ObjectIdentityTests
{
    [Fact]
    public void Portable_keys_round_trip_and_are_injective_for_dots_brackets_and_literal_key_patterns()
    {
        var parts = new[] { "a", "b", "a.b", "[a]", "a]", "[", "]", "].", "[a].[b]", "dbo", "Table" };
        var identities = parts.SelectMany(schema => parts.Select(name => new DatabaseObjectIdentity(schema, name))).ToArray();
        Assert.Equal(identities.Length, identities.Select(identity => identity.Key).Distinct(StringComparer.Ordinal).Count());
        foreach (var identity in identities) Assert.Equal(identity, DatabaseObjectIdentity.ParseKey(identity.Key));
        Assert.Equal("dbo.Table", new DatabaseObjectIdentity("dbo", "Table").Key);
        Assert.Equal("[a.b].[c]", new DatabaseObjectIdentity("a.b", "c").Key);
        Assert.Equal("[a].[b.c]", new DatabaseObjectIdentity("a", "b.c").Key);
        Assert.Equal("[[a]]].[b]", new DatabaseObjectIdentity("[a]", "b").Key);
        Assert.NotEqual(new DatabaseObjectIdentity("a.b", "c"), new DatabaseObjectIdentity("a", "b.c"));
    }

    [Fact]
    public void Ambiguous_or_broken_legacy_keys_fail_instead_of_selecting_another_object()
    {
        foreach (var key in new[] { "a.b.c", "Table", ".Table", "dbo.", "[a].[b", "[a].[b]tail", "[a].b", "[a].[b]" })
            Assert.Throws<ArgumentException>(() => DatabaseObjectIdentity.ParseKey(key));
        Assert.Equal(new DatabaseObjectIdentity("dbo", "Odd]Name"), DatabaseObjectIdentity.ParseKey("dbo.Odd]Name"));
    }

    [Theory]
    [InlineData(DbEngine.SqlServer, "SELECT TOP 3 * FROM [a.b].[c]", "SELECT TOP 3 * FROM [a].[b.c]")]
    [InlineData(DbEngine.PostgreSql, "SELECT * FROM \"a.b\".\"c\" LIMIT 3", "SELECT * FROM \"a\".\"b.c\" LIMIT 3")]
    [InlineData(DbEngine.MySql, "SELECT * FROM `a.b`.`c` LIMIT 3", "SELECT * FROM `a`.`b.c` LIMIT 3")]
    public void Sampling_quotes_raw_parts_for_each_provider_without_splitting_a_display_key(DbEngine engine, string expectedOne, string expectedTwo)
    {
        var one = new DatabaseObjectIdentity("a.b", "c");
        var two = new DatabaseObjectIdentity("a", "b.c");
        Assert.Equal(expectedOne, DbSchemaService.SampleQuery(engine, one));
        Assert.Equal(expectedTwo, DbSchemaService.SampleQuery(engine, two));
        Assert.NotEqual(DbSchemaService.SampleQuery(engine, one), DbSchemaService.SampleQuery(engine, two));
    }

    [Fact]
    public void Identity_manifest_preserves_raw_parts_in_private_exports_and_sample_free_review()
    {
        var identity = new DatabaseObjectIdentity("a.b", "c]");
        var schema = JsonSerializer.Serialize(new
        {
            MetadataFormat = new { Version = 2 },
            Tables = new Dictionary<string, object> { [identity.Key] = new[] { new { Column = "Id", Type = "int" } } },
            TableIdentities = new Dictionary<string, DatabaseObjectIdentity> { [identity.Key] = identity },
            SampleData = new Dictionary<string, object> { [identity.Key] = new[] { new { Value = "SAMPLE_SENTINEL_IDENTITY" } } }
        });
        var safe = SchemaPrivacy.WithoutSamples(schema);
        using var document = JsonDocument.Parse(safe);
        Assert.Equal("a.b", document.RootElement.GetProperty("TableIdentities").GetProperty(identity.Key).GetProperty("Schema").GetString());
        Assert.Equal("c]", document.RootElement.GetProperty("TableIdentities").GetProperty(identity.Key).GetProperty("Name").GetString());
        Assert.DoesNotContain("SAMPLE_SENTINEL_IDENTITY", safe);
        Assert.Contains("TableIdentities", SchemaEvidenceSummary.Create(safe));
        Assert.DoesNotContain("SAMPLE_SENTINEL_IDENTITY", SchemaDiagramHtml.Create(schema));
    }
}
