using System.Text.Json;
using Xunit;

namespace Tangent.Cli.Tests;

/// <summary>The SARIF log has to be something GitHub code scanning will accept, so the shape is asserted.</summary>
[Collection(OrdersCollection.Name)]
public sealed class SarifTests(OrdersFixture fixture)
{
    private JsonElement Run => JsonDocument.Parse(fixture.Sarif).RootElement.GetProperty("runs")[0];

    [Fact]
    public void IsSarif210()
    {
        JsonElement log = JsonDocument.Parse(fixture.Sarif).RootElement;
        Assert.Equal("2.1.0", log.GetProperty("version").GetString());
        Assert.Contains("sarif-schema-2.1.0", log.GetProperty("$schema").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribesTheDriverAndEveryRule()
    {
        JsonElement driver = Run.GetProperty("tool").GetProperty("driver");
        Assert.Equal("Tangent", driver.GetProperty("name").GetString());
        Assert.False(string.IsNullOrWhiteSpace(driver.GetProperty("version").GetString()));

        JsonElement[] rules = driver.GetProperty("rules").EnumerateArray().ToArray();
        Assert.Equal(Tangent.Analyzers.TangentDiagnostics.All.Length, rules.Length);

        foreach (JsonElement rule in rules)
        {
            Assert.StartsWith("TNG", rule.GetProperty("id").GetString()!, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(rule.GetProperty("shortDescription").GetProperty("text").GetString()));
            Assert.Contains(
                rule.GetProperty("defaultConfiguration").GetProperty("level").GetString(),
                new[] { "error", "warning", "note" });
            Assert.StartsWith("https://", rule.GetProperty("helpUri").GetString()!, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void HasOneResultPerJsonRow()
    {
        int jsonRows = JsonDocument.Parse(fixture.Json).RootElement.GetArrayLength();
        Assert.Equal(jsonRows, Run.GetProperty("results").GetArrayLength());
    }

    [Fact]
    public void LocatesResultsRelativeToTheSolutionRoot()
    {
        Assert.Contains(
            "samples/Orders/",
            Run.GetProperty("originalUriBaseIds").GetProperty("SRCROOT").GetProperty("uri").GetString()!,
            StringComparison.Ordinal);

        foreach (JsonElement result in Run.GetProperty("results").EnumerateArray())
        {
            Assert.Contains(result.GetProperty("level").GetString(), new[] { "error", "warning", "note" });

            JsonElement location = result.GetProperty("locations")[0].GetProperty("physicalLocation");
            string uri = location.GetProperty("artifactLocation").GetProperty("uri").GetString()!;
            Assert.StartsWith("src/", uri, StringComparison.Ordinal);
            Assert.False(uri.Contains(':'), "URIs must be relative to SRCROOT, not absolute: " + uri);
            Assert.Equal("SRCROOT", location.GetProperty("artifactLocation").GetProperty("uriBaseId").GetString());

            JsonElement region = location.GetProperty("region");
            Assert.True(region.GetProperty("startLine").GetInt32() >= 1);
            Assert.True(region.GetProperty("startColumn").GetInt32() >= 1);
            Assert.True(region.GetProperty("endLine").GetInt32() >= region.GetProperty("startLine").GetInt32());
        }
    }

    [Fact]
    public void CopiesTheDiagnosticPropertyBag()
    {
        JsonElement result = Run.GetProperty("results").EnumerateArray()
            .First(r => r.GetProperty("ruleId").GetString() == "TNG0101");

        JsonElement properties = result.GetProperty("properties");
        Assert.Equal("Controller", properties.GetProperty("contract").GetString());
        Assert.Equal("symbol", properties.GetProperty("tier").GetString());
        Assert.Equal("add-modifier", properties.GetProperty("fix").GetString());
        Assert.Equal("sealed", properties.GetProperty("fixArg").GetString());
    }
}
