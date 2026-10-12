using System.Text.Json;
using Xunit;

namespace Tangent.Cli.Tests;

/// <summary><c>tangent check --explain</c> against <c>samples/Orders</c>.</summary>
[Collection(OrdersCollection.Name)]
public sealed class ExplainTests(OrdersFixture fixture)
{
    private const string Customers = "src/Orders.Api/Controllers/CustomersController.cs";
    private const string LegacyOrders = "src/Orders.Api/Controllers/LegacyOrdersController.cs";

    private JsonElement[] Rows => JsonDocument.Parse(fixture.ExplainJson).RootElement.EnumerateArray().ToArray();

    private JsonElement Row(string code, string file, int line) => Rows.First(row =>
        row.GetProperty("code").GetString() == code &&
        row.GetProperty("file").GetString() == file &&
        row.GetProperty("line").GetInt32() == line);

    [Fact]
    public void DoesNotChangeTheResult()
    {
        Assert.Equal(fixture.Check.ExitCode, fixture.ExplainCheck.ExitCode);
        Assert.Equal(JsonDocument.Parse(fixture.Json).RootElement.GetArrayLength(), Rows.Length);
    }

    [Fact]
    public void ExplainsAContractRule()
    {
        JsonElement explain = Row("TNG0101", Customers, 15).GetProperty("explain");

        Assert.Equal("require sealed", explain.GetProperty("rule").GetString());
        Assert.Equal(".tangent/controllers.tan:7", explain.GetProperty("declared").GetString());
        Assert.Equal(
            "contract Controller matches class in Api where name ends with \"Controller\"",
            explain.GetProperty("appliesTo").GetString());
        Assert.Equal("public class CustomersController : ApiControllerBase", explain.GetProperty("source").GetString());
    }

    [Fact]
    public void ExplainsAMemberRuleInsideItsBlock()
    {
        JsonElement explain = Row("TNG0203", LegacyOrders, 28).GetProperty("explain");

        Assert.Equal("methods where public { forbid new HttpClient }", explain.GetProperty("rule").GetString());
        Assert.Equal(".tangent/controllers.tan:20", explain.GetProperty("declared").GetString());
    }

    [Fact]
    public void ExplainsALayerRule()
    {
        JsonElement explain = Row("TNG0202", LegacyOrders, 32).GetProperty("explain");

        Assert.Equal("Api -> Application, Domain", explain.GetProperty("rule").GetString());
        Assert.Equal(".tangent/project.tan", explain.GetProperty("declared").GetString());
        Assert.Equal("layer Api = Orders.Api.**", explain.GetProperty("appliesTo").GetString());
    }

    [Fact]
    public void LeavesFindingsWithoutARuleUnexplained()
    {
        Assert.False(Row("TNG0001", LegacyOrders, 16).TryGetProperty("explain", out _));
    }

    [Fact]
    public void LeavesTheDefaultReportUnchanged()
    {
        foreach (JsonElement row in JsonDocument.Parse(fixture.Json).RootElement.EnumerateArray())
        {
            Assert.False(row.TryGetProperty("explain", out _));
        }

        Assert.DoesNotContain("\"ruleText\"", fixture.Sarif, StringComparison.Ordinal);
        Assert.DoesNotContain("applies to", fixture.Check.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void PrintsTheRuleAndACaretUnderTheOffendingCode()
    {
        string[] lines = fixture.ExplainCheck.Out.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        int at = Array.FindIndex(lines, line =>
            line.Contains("TNG0101 " + Customers + ":15:14", StringComparison.Ordinal));
        Assert.True(at >= 0, fixture.ExplainCheck.Out);

        Assert.Equal("      rule        require sealed", lines[at + 1]);
        Assert.Equal("      declared    .tangent/controllers.tan:7", lines[at + 2]);
        Assert.StartsWith("      applies to  contract Controller matches", lines[at + 3], StringComparison.Ordinal);
        Assert.Equal("      15 | public class CustomersController : ApiControllerBase", lines[at + 4]);
        Assert.Equal("         |              ^^^^^^^^^^^^^^^^^^^", lines[at + 5]);
    }

    [Fact]
    public void AddsTheRuleTextToSarif()
    {
        JsonElement result = JsonDocument.Parse(fixture.ExplainSarif).RootElement
            .GetProperty("runs")[0].GetProperty("results").EnumerateArray()
            .First(r => r.GetProperty("ruleId").GetString() == "TNG0101" &&
                        r.GetProperty("locations")[0].GetProperty("physicalLocation")
                            .GetProperty("artifactLocation").GetProperty("uri").GetString() == Customers);

        JsonElement properties = result.GetProperty("properties");
        Assert.Equal("require sealed", properties.GetProperty("ruleText").GetString());
        Assert.Equal(".tangent/controllers.tan:7", properties.GetProperty("ruleDeclared").GetString());
    }
}
