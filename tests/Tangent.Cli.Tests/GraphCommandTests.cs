using System.Text.Json;
using Xunit;

namespace Tangent.Cli.Tests;

/// <summary>
/// <c>tangent graph</c> has to produce exactly what <c>viewer/src/graph/types.ts</c> describes, so these
/// assertions mirror that file field for field.
/// </summary>
[Collection(OrdersCollection.Name)]
public sealed class GraphCommandTests(OrdersFixture fixture)
{
    private JsonElement Bound => JsonDocument.Parse(fixture.GraphJson).RootElement;

    private JsonElement Unbound => JsonDocument.Parse(fixture.UnboundGraphJson).RootElement;

    [Fact]
    public void HasTheDocumentHeader()
    {
        Assert.Equal(1, Bound.GetProperty("version").GetInt32());
        Assert.Equal("Orders", Bound.GetProperty("project").GetString());
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$", Bound.GetProperty("generatedAt").GetString()!);
    }

    [Fact]
    public void HasTheFourLayersWithTheirAllowedDependencies()
    {
        Dictionary<string, string[]> layers = Bound.GetProperty("layers").EnumerateArray().ToDictionary(
            layer => layer.GetProperty("id").GetString()!,
            layer => layer.GetProperty("allowedDependencies").EnumerateArray().Select(d => d.GetString()!).ToArray(),
            StringComparer.Ordinal);

        Assert.Equal(4, layers.Count);
        Assert.Equal(["Application", "Domain"], layers["Api"]);
        Assert.Equal(["Domain"], layers["Application"]);
        Assert.Equal(["Application", "Domain"], layers["Infrastructure"]);
        Assert.Empty(layers["Domain"]);

        Assert.Equal(
            ["Orders.Api.**"],
            Bound.GetProperty("layers").EnumerateArray()
                .First(l => l.GetProperty("id").GetString() == "Api")
                .GetProperty("namespaces").EnumerateArray().Select(n => n.GetString()!).ToArray());
    }

    [Fact]
    public void ListsEveryContractWithItsOwnRuleCount()
    {
        JsonElement[] contracts = Bound.GetProperty("contracts").EnumerateArray().ToArray();
        string[] ids = contracts.Select(c => c.GetProperty("id").GetString()!).ToArray();

        Assert.Contains("Controller", ids);
        Assert.Contains("AggregateRoot", ids);

        JsonElement aggregate = contracts.First(c => c.GetProperty("id").GetString() == "AggregateRoot");
        Assert.Equal("Entity", aggregate.GetProperty("extends").GetString());
        Assert.EndsWith("entities.tan", aggregate.GetProperty("file").GetString()!, StringComparison.Ordinal);
        Assert.True(aggregate.GetProperty("ruleCount").GetInt32() > 0);
    }

    [Fact]
    public void HasNodesWithMembersAndCounts()
    {
        JsonElement[] nodes = Bound.GetProperty("nodes").EnumerateArray().ToArray();
        Assert.True(nodes.Length >= 15, $"expected at least 15 bound nodes, got {nodes.Length}");

        JsonElement controller = nodes.Single(n =>
            n.GetProperty("id").GetString() == "Orders.Api.Controllers.CustomersController");

        Assert.Equal("CustomersController", controller.GetProperty("name").GetString());
        Assert.Equal("class", controller.GetProperty("kind").GetString());
        Assert.Equal("Api", controller.GetProperty("layer").GetString());
        Assert.Equal(["Controller"], controller.GetProperty("contracts").EnumerateArray().Select(c => c.GetString()!).ToArray());
        Assert.Equal("src/Orders.Api/Controllers/CustomersController.cs", controller.GetProperty("file").GetString());
        Assert.True(controller.GetProperty("violations").GetInt32() > 0);
        Assert.Equal(1, controller.GetProperty("exemptions").GetInt32());

        JsonElement member = controller.GetProperty("members").EnumerateArray()
            .Single(m => m.GetProperty("name").GetString() == "GetByIdAsync");
        Assert.Equal("method", member.GetProperty("kind").GetString());
        Assert.Contains("Task<ActionResult<CustomerViewModel>>", member.GetProperty("signature").GetString()!, StringComparison.Ordinal);

        // Every node's layer must be one that layers[] declares, or null.
        var layerIds = Bound.GetProperty("layers").EnumerateArray().Select(l => l.GetProperty("id").GetString()!).ToHashSet(StringComparer.Ordinal);
        foreach (JsonElement node in nodes)
        {
            JsonElement layer = node.GetProperty("layer");
            if (layer.ValueKind != JsonValueKind.Null) Assert.Contains(layer.GetString()!, layerIds);
        }
    }

    [Fact]
    public void HasNoDanglingEdges()
    {
        foreach (JsonElement graph in new[] { Bound, Unbound })
        {
            var ids = graph.GetProperty("nodes").EnumerateArray()
                .Select(n => n.GetProperty("id").GetString()!).ToHashSet(StringComparer.Ordinal);

            Assert.NotEmpty(graph.GetProperty("edges").EnumerateArray());
            foreach (JsonElement edge in graph.GetProperty("edges").EnumerateArray())
            {
                Assert.Contains(edge.GetProperty("from").GetString()!, ids);
                Assert.Contains(edge.GetProperty("to").GetString()!, ids);
                Assert.True(edge.GetProperty("count").GetInt32() >= 1);
                Assert.Equal("reference", edge.GetProperty("kind").GetString());
            }
        }
    }

    [Fact]
    public void IncludeUnboundShowsTheLayerViolations()
    {
        // The types that break layering - OrdersDbContext, CacheKeys, INotificationDispatcher - are bound
        // to no contract, so the illegal edges only exist once unbound types are in the graph.
        JsonElement[] illegal = Unbound.GetProperty("edges").EnumerateArray()
            .Where(e => e.GetProperty("violatesLayering").GetBoolean()).ToArray();

        Assert.NotEmpty(illegal);
        Assert.Contains(illegal, e =>
            e.GetProperty("from").GetString() == "Orders.Api.Controllers.ReportsController" &&
            e.GetProperty("to").GetString() == "Orders.Infrastructure.Persistence.OrdersDbContext");

        Assert.True(
            Unbound.GetProperty("nodes").GetArrayLength() > Bound.GetProperty("nodes").GetArrayLength(),
            "--include-unbound must add nodes");
    }

    [Fact]
    public void ViolationsMatchTheCheckReport()
    {
        int reported = JsonDocument.Parse(fixture.Json).RootElement.GetArrayLength();
        Assert.Equal(reported, Bound.GetProperty("violations").GetArrayLength());
        Assert.Equal(reported, Unbound.GetProperty("violations").GetArrayLength());

        JsonElement violation = Bound.GetProperty("violations").EnumerateArray()
            .First(v => v.GetProperty("code").GetString() == "TNG0101");

        Assert.Equal("Orders.Api.Controllers.CustomersController", violation.GetProperty("node").GetString());
        Assert.Equal("Controller/type/modifier#0", violation.GetProperty("ruleId").GetString());
        Assert.Equal("warning", violation.GetProperty("severity").GetString());
    }

    [Fact]
    public void InlinesTheRuleTextTheViewerShows()
    {
        JsonElement[] rules = Bound.GetProperty("rules").EnumerateArray().ToArray();
        Assert.NotEmpty(rules);

        JsonElement inherits = rules.Single(r => r.GetProperty("id").GetString() == "Controller/type/inherits#0");
        Assert.Equal("Controller", inherits.GetProperty("contract").GetString());
        Assert.Equal("require inherits ApiControllerBase", inherits.GetProperty("text").GetString());
        Assert.Equal(0, inherits.GetProperty("tier").GetInt32());

        // Every violation's rule id must resolve, layer violations aside (they are not contract rules).
        var ruleIds = rules.Select(r => r.GetProperty("id").GetString()!).ToHashSet(StringComparer.Ordinal);
        foreach (JsonElement violation in Bound.GetProperty("violations").EnumerateArray())
        {
            string id = violation.GetProperty("ruleId").GetString()!;
            if (id.Length == 0 || id.StartsWith("layer:", StringComparison.Ordinal)) continue;
            Assert.Contains(id, ruleIds);
        }
    }

    [Fact]
    public void IsDeterministic()
    {
        string second = Path.Combine(fixture.TempDirectory, "again.graph.json");
        CliRun run = Cli.Run("graph", Cli.OrdersSample, "-o", second, "--no-color");
        Assert.Equal(ExitCodes.Clean, run.ExitCode);

        static string WithoutTimestamp(string json) =>
            string.Join('\n', json.ReplaceLineEndings("\n").Split('\n').Where(l => !l.Contains("\"generatedAt\"", StringComparison.Ordinal)));

        Assert.Equal(WithoutTimestamp(fixture.GraphJson), WithoutTimestamp(File.ReadAllText(second)));
    }
}
