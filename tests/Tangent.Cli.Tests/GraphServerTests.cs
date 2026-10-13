using System.Net;
using System.Text;
using Tangent.Cli.Serve;
using Xunit;

namespace Tangent.Cli.Tests;

/// <summary>
/// The server behind <c>tangent graph --serve</c>. Any page in the browser can send a request to
/// <c>127.0.0.1</c>, so the graph goes only to a request that carries the run's token, names this server as
/// its host, and comes from no other site.
/// </summary>
public sealed class GraphServerTests
{
    private const string Graph = """{"version":2,"project":"Test"}""";

    internal static ViewerAssets Viewer { get; } = new(new Dictionary<string, byte[]>
    {
        ["index.html"] = Encoding.UTF8.GetBytes("<!doctype html><title>Tangent</title>"),
        ["assets/index.js"] = Encoding.UTF8.GetBytes("console.log('tangent');"),
    });

    [Fact]
    public async Task ServesTheGraphAtTheTokenAddress()
    {
        await using GraphServer server = await GraphServer.StartAsync(Graph, Viewer, 0, CancellationToken.None);
        using var http = new HttpClient();

        using HttpResponseMessage response = await http.GetAsync(server.GraphUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Graph, await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.StartsWith($"http://127.0.0.1:{server.Port}/?graph=/api/graph/", server.ViewerUrl, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/graph")]
    [InlineData("/api/graph/")]
    [InlineData("/api/graph/00000000000000000000000000000000")]
    [InlineData("/api/violations")]
    public async Task RefusesAnAddressWithoutTheToken(string path)
    {
        await using GraphServer server = await GraphServer.StartAsync(Graph, Viewer, 0, CancellationToken.None);
        using var http = new HttpClient();

        using HttpResponseMessage response = await http.GetAsync($"http://127.0.0.1:{server.Port}{path}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EachRunHasItsOwnToken()
    {
        await using GraphServer first = await GraphServer.StartAsync(Graph, Viewer, 0, CancellationToken.None);
        await using GraphServer second = await GraphServer.StartAsync(Graph, Viewer, 0, CancellationToken.None);

        Assert.Equal(32, first.Token.Length);
        Assert.NotEqual(first.Token, second.Token);
    }

    [Fact]
    public async Task RefusesARequestForAnotherHost()
    {
        // A rebound DNS name reaches 127.0.0.1 with its own name in Host.
        await using GraphServer server = await GraphServer.StartAsync(Graph, Viewer, 0, CancellationToken.None);
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, server.GraphUrl);
        request.Headers.Host = $"attacker.example:{server.Port}";

        using HttpResponseMessage response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("https://attacker.example", HttpStatusCode.Forbidden)]
    [InlineData("null", HttpStatusCode.Forbidden)]
    [InlineData("http://127.0.0.1:{port}", HttpStatusCode.OK)]
    [InlineData("http://localhost:{port}", HttpStatusCode.OK)]
    public async Task AcceptsOnlyItsOwnOrigin(string origin, HttpStatusCode expected)
    {
        await using GraphServer server = await GraphServer.StartAsync(Graph, Viewer, 0, CancellationToken.None);
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, server.GraphUrl);
        request.Headers.Add("Origin", origin.Replace("{port}", server.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));

        using HttpResponseMessage response = await http.SendAsync(request);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task RefusesAWrite()
    {
        await using GraphServer server = await GraphServer.StartAsync(Graph, Viewer, 0, CancellationToken.None);
        using var http = new HttpClient();

        using HttpResponseMessage response = await http.PostAsync(server.GraphUrl, new StringContent("{}"));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Contains("GET", response.Content.Headers.Allow);
    }

    [Fact]
    public void TheBuildEmbedsTheViewerWithoutMapsOrLocalGraphs()
    {
        // The build embeds viewer/dist only when it exists; CI builds the viewer first.
        string dist = Path.Combine(Cli.RepositoryRoot, "viewer", "dist");
        if (!File.Exists(Path.Combine(dist, "index.html"))) return;

        string[] paths = ViewerAssets.FromAssembly().Paths.ToArray();

        Assert.Contains("index.html", paths);
        Assert.Contains("sample.graph.json", paths);
        Assert.Contains(paths, p => p.StartsWith("assets/", StringComparison.Ordinal) && p.EndsWith(".js", StringComparison.Ordinal));
        Assert.DoesNotContain(paths, p => p.EndsWith(".map", StringComparison.Ordinal));
        Assert.DoesNotContain(paths, p => p.EndsWith(".local.graph.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ServesOnlyTheEmbeddedViewerFiles()
    {
        await using GraphServer server = await GraphServer.StartAsync(Graph, Viewer, 0, CancellationToken.None);
        using var http = new HttpClient();
        string root = $"http://127.0.0.1:{server.Port}";

        using HttpResponseMessage index = await http.GetAsync(root + "/");
        Assert.Equal("text/html", index.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<title>Tangent</title>", await index.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using HttpResponseMessage script = await http.GetAsync(root + "/assets/index.js");
        Assert.Equal("text/javascript", script.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", string.Join(",", script.Headers.GetValues("X-Content-Type-Options")));

        using HttpResponseMessage missing = await http.GetAsync(root + "/assets/other.js");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        using HttpResponseMessage project = await http.GetAsync(root + "/Tangent.Cli.dll");
        Assert.Equal(HttpStatusCode.NotFound, project.StatusCode);
    }
}
