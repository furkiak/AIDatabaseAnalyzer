using System.Text.Json;
using AIDatabaseAnalyzer.Services;

namespace AIDatabaseAnalyzer.ConnectionTests;

public class PrivacyAndPromptTests
{
    [Fact]
    public void CloudPayloadNeverIncludesSampleRowsEvenWhenTheyContainSecrets()
    {
        const string original = """
            {"Tables":{"Orders":[{"Column":"Id","Type":"int"}]},"SampleData":{"Orders":[{"Card":"SAMPLE_SENTINEL_8462"}]},"Indexes":[]}
            """;
        var payload = SchemaPrivacy.WithoutSamples(original);
        Assert.DoesNotContain("SAMPLE_SENTINEL_8462", payload);
        using var json = JsonDocument.Parse(payload);
        Assert.False(json.RootElement.TryGetProperty("SampleData", out _));
        Assert.True(json.RootElement.TryGetProperty("Tables", out _));
        Assert.True(json.RootElement.TryGetProperty("Indexes", out _));
    }

    [Fact]
    public void SchemaIsMarkedAsUntrustedAndUnobservedEvidenceIsDisclaimed()
    {
        var prompt = AnalysisPromptPolicy.Topic("Review indexes", "{\"Tables\":{}}");
        Assert.Contains("untrusted source data", prompt);
        Assert.Contains("Not assessed", prompt);
        Assert.Contains("query plans", prompt);
        Assert.Contains("<schema_json>", prompt);
    }

    [Fact]
    public async Task GeminiKeyIsAHeaderAndNeverAppearsInUrlOrPayload()
    {
        const string secret = "KEY_SENTINEL_37";
        using var request = GeminiApiRequest.Create("gemini-3.1-flash-lite-preview", secret, "Review metadata");
        Assert.Equal("https", request.RequestUri!.Scheme);
        Assert.Equal("generativelanguage.googleapis.com", request.RequestUri.Host);
        Assert.DoesNotContain(secret, request.RequestUri.ToString());
        Assert.DoesNotContain("?key=", request.RequestUri.ToString());
        Assert.Equal(secret, request.Headers.GetValues("x-goog-api-key").Single());
        Assert.DoesNotContain(secret, await request.Content!.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("http://localhost:11434", true)]
    [InlineData("http://127.0.0.1:11434", true)]
    [InlineData("http://[::1]:11434", true)]
    [InlineData("https://localhost.example.com", false)]
    [InlineData("http://192.168.1.20:11434", false)]
    [InlineData("http://user:secret@localhost:11434", false)]
    public void OnlyActualLoopbackOllamaSkipsRemoteConsent(string url, bool expected) =>
        Assert.Equal(expected, ModelDestination.IsLoopbackOllama(url));

    [Fact]
    public void DiagramKeepsDatabaseIdentifiersOutOfHtmlAndNeverLoadsCdnScripts()
    {
        const string json = """
            {"Tables":{"dbo.Orders</script><script>alert(1)</script>":[{"Column":"Name","Type":"nvarchar"}]},"ForeignKeys":[{"From":"dbo.Orders</script><script>alert(1)</script>","To":"dbo.Users","Name":"FK_Orders_Users"}],"SampleData":{"dbo.Users":[{"Private":"SAMPLE_SENTINEL_21"}]}}
            """;
        var html = SchemaDiagramHtml.Create(json);
        Assert.DoesNotContain("SAMPLE_SENTINEL_21", html);
        Assert.DoesNotContain("</script><script>alert(1)", html);
        Assert.DoesNotContain("cdn.jsdelivr.net", html);
        Assert.Contains("\\u003C/script\\u003E", html);
        Assert.Contains("FK_Orders_Users", html);
        Assert.Contains("textContent = value", html);
    }

    [Fact]
    public void EvidenceManifestProvidesJsonPathsWithoutSampleValues()
    {
        var payload = SchemaPrivacy.WithoutSamples("""
            {"Tables":{"dbo.Users":[{"Column":"Id","Type":"int","MaxLen":4}]},"ForeignKeys":[{"Name":"FK_User_Team","From":"dbo.Users","To":"dbo.Teams"}],"SampleData":{"dbo.Users":[{"Email":"SAMPLE_SENTINEL_55"}]}}
            """);
        var manifest = SchemaEvidenceSummary.Create(payload);
        Assert.Contains("Tables[\"dbo.Users\"]", manifest);
        Assert.Contains("ForeignKeys[0]", manifest);
        Assert.DoesNotContain("SAMPLE_SENTINEL_55", manifest);
    }

    [Fact]
    public void LargeSchemaIsSplitWithoutDroppingTablesOrRepeatedRelationshipEvidence()
    {
        var tables = Enumerable.Range(0, 20).ToDictionary(
            n => "dbo.T" + n,
            n => new[] { new { Column = "Column" + n, Type = "nvarchar", MaxLen = 200 } });
        var schema = JsonSerializer.Serialize(new { Tables = tables,
            ForeignKeys = new[] { new { Name = "FK", From = "dbo.T0", To = "dbo.T1" } } });
        var chunks = SchemaChunker.Split(schema, 430);
        Assert.True(chunks.Count > 1);
        var names = chunks.SelectMany(chunk => JsonDocument.Parse(chunk).RootElement.GetProperty("Tables").EnumerateObject().Select(p => p.Name)).ToList();
        Assert.Equal(20, names.Count);
        Assert.Equal(20, names.Distinct().Count());
        foreach (var chunk in chunks)
        {
            Assert.True(chunk.Length <= 430);
            using var json = JsonDocument.Parse(chunk);
            Assert.Equal("FK", json.RootElement.GetProperty("ForeignKeys")[0].GetProperty("Name").GetString());
        }
    }

    [Fact]
    public void PackagedOfflineDemoIsSyntheticAndCanRenderWithoutNetworkOrModel()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "demo-schema.json"));
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("Demo").GetBoolean());
        Assert.Equal(3, document.RootElement.GetProperty("Tables").EnumerateObject().Count());
        Assert.False(document.RootElement.TryGetProperty("SampleData", out _));
        var html = SchemaDiagramHtml.Create(json);
        Assert.Contains("FK_Orders_Customers", html);
        Assert.DoesNotContain("https://", html);
    }
}
