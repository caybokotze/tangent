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
    public void PointsAtTheHostedViewer()
    {
        Assert.Equal(ExitCodes.Clean, fixture.Graph.ExitCode);
        Assert.Contains("Open https://caybokotze.github.io/tangent/ and choose this file.", fixture.Graph.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void HasTheDocumentHeader()
    {
        Assert.Equal(2, Bound.GetProperty("version").GetInt32());
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
        Assert.Equal(1, controller.GetProperty("ignored").GetInt32());

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
    public void FileTableCoversEverySourceFileAndEveryContract()
    {
        JsonElement[] files = Bound.GetProperty("files").EnumerateArray().ToArray();
        string[] paths = files.Select(f => f.GetProperty("path").GetString()!).ToArray();

        // Ids are the index, and the table is sorted by path.
        for (int i = 0; i < files.Length; i++) Assert.Equal(i, files[i].GetProperty("id").GetInt32());
        Assert.Equal(paths.Order(StringComparer.Ordinal), paths);

        string[] expected = Directory.EnumerateFiles(Path.Combine(Cli.OrdersSample, "src"), "*.cs", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(Cli.OrdersSample, p).Replace('\\', '/'))
            .Where(p => !p.Contains("/obj/", StringComparison.Ordinal) && !p.Contains("/bin/", StringComparison.Ordinal))
            .ToArray();
        foreach (string path in expected) Assert.Contains(path, paths);

        Assert.All(paths, p => Assert.False(Path.IsPathRooted(p), $"{p} is outside the solution root"));

        JsonElement[] contracts = files.Where(f => f.GetProperty("kind").GetString() == "contract").ToArray();
        Assert.Contains(contracts, f => f.GetProperty("path").GetString() == ".tangent/controllers.tan");
        Assert.All(contracts, f => Assert.Equal("contract", f.GetProperty("status").GetString()));
    }

    [Fact]
    public void FileStatusReflectsContractCoverage()
    {
        Dictionary<string, JsonElement> files = Bound.GetProperty("files").EnumerateArray()
            .ToDictionary(f => f.GetProperty("path").GetString()!, StringComparer.Ordinal);

        JsonElement controller = files["src/Orders.Api/Controllers/CustomersController.cs"];
        Assert.Equal("tracked", controller.GetProperty("status").GetString());
        Assert.Contains("Orders.Api.Controllers.CustomersController",
            controller.GetProperty("types").EnumerateArray().Select(t => t.GetString()));
        Assert.True(controller.GetProperty("violations").GetInt32() > 0);

        Assert.Equal("untracked", files["src/Orders.Api/Program.cs"].GetProperty("status").GetString());
        Assert.Equal("untracked", files["src/Orders.Infrastructure/StaticUserContext.cs"].GetProperty("status").GetString());

        // Coverage does not depend on --include-unbound: the file table is the same either way.
        string Statuses(JsonElement graph) => string.Join('\n', graph.GetProperty("files").EnumerateArray()
            .Select(f => f.GetProperty("path").GetString() + " " + f.GetProperty("status").GetString()));
        Assert.Equal(Statuses(Bound), Statuses(Unbound));
    }

    [Fact]
    public void NodesAndViolationsPointIntoTheFileTable()
    {
        foreach (JsonElement graph in new[] { Bound, Unbound })
        {
            string[] paths = graph.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("path").GetString()!).ToArray();

            foreach (JsonElement item in graph.GetProperty("nodes").EnumerateArray().Concat(graph.GetProperty("violations").EnumerateArray()))
            {
                int id = item.GetProperty("fileId").GetInt32();

                // Types from projects outside the solution root (the sample references Tangent.Attributes
                // directly) keep an absolute path and have no file table entry.
                if (id == -1)
                {
                    Assert.True(Path.IsPathRooted(item.GetProperty("file").GetString()!));
                    continue;
                }

                Assert.InRange(id, 0, paths.Length - 1);
                Assert.Equal(item.GetProperty("file").GetString(), paths[id]);
            }
        }
    }

    [Fact]
    public void WithoutIncludeSourceNoSourceOrRootIsEmbedded()
    {
        Assert.False(Bound.TryGetProperty("root", out _));
        Assert.All(Bound.GetProperty("violations").EnumerateArray(), v => Assert.False(v.TryGetProperty("snippet", out _)));
        Assert.All(Bound.GetProperty("files").EnumerateArray(), f => Assert.False(f.TryGetProperty("text", out _)));
    }

    [Fact]
    public void IncludeSourceEmbedsSnippetsContractTextAndTheRoot()
    {
        Assert.Equal(Cli.OrdersSample.Replace('\\', '/'), Unbound.GetProperty("root").GetString());

        foreach (JsonElement violation in Unbound.GetProperty("violations").EnumerateArray())
        {
            JsonElement snippet = violation.GetProperty("snippet");
            int start = snippet.GetProperty("startLine").GetInt32();
            string[] lines = snippet.GetProperty("lines").EnumerateArray().Select(l => l.GetString()!).ToArray();
            int line = violation.GetProperty("line").GetInt32();

            Assert.InRange(line, start, start + lines.Length - 1);
            Assert.True(lines.Length <= 31, "a snippet is at most 15 lines either side of the violation");

            string[] actual = File.ReadAllLines(Path.Combine(Cli.OrdersSample, violation.GetProperty("file").GetString()!));
            Assert.Equal(actual[line - 1], lines[line - start]);
        }

        JsonElement controllers = Unbound.GetProperty("files").EnumerateArray()
            .Single(f => f.GetProperty("path").GetString() == ".tangent/controllers.tan");
        Assert.Contains("contract Controller", controllers.GetProperty("text").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void ListsEveryIgnoredViolation()
    {
        // Like violations[], ignored[] lists every ignored violation of the run, even for a type that is not in
        // nodes[] (an ignored type with no contract left is unbound), so count against the unbound graph.
        JsonElement[] ignored = Bound.GetProperty("ignored").EnumerateArray().ToArray();
        int ignoredNodes = Unbound.GetProperty("nodes").EnumerateArray().Sum(n => n.GetProperty("ignored").GetInt32());
        Assert.Equal(ignoredNodes, ignored.Length);
        Assert.Equal(ignored.Length, Unbound.GetProperty("ignored").GetArrayLength());

        JsonElement customers = ignored.First(e =>
            e.GetProperty("node").GetString() == "Orders.Api.Controllers.CustomersController");
        Assert.False(string.IsNullOrWhiteSpace(customers.GetProperty("reason").GetString()));
        Assert.DoesNotContain("Violation ignored", customers.GetProperty("reason").GetString()!, StringComparison.Ordinal);
        Assert.NotEqual(string.Empty, customers.GetProperty("ruleId").GetString());
        Assert.True(customers.GetProperty("fileId").GetInt32() >= 0);
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
