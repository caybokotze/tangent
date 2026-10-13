using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Tangent.Cli.Serve;

/// <summary>
/// The local server behind <c>tangent graph --serve</c>: the viewer and one graph, on <c>127.0.0.1</c> only.
/// <para>
/// Any page open in the browser can send a request to <c>127.0.0.1</c>, so the graph is guarded three ways.
/// It is served only at <c>/api/graph/&lt;token&gt;</c>, with a random token per run. A request whose
/// <c>Host</c> is not this server is refused, which stops DNS rebinding. A request whose <c>Origin</c> is
/// another site is refused, and no response carries a CORS header.
/// </para>
/// <para>
/// The server reads no file from disk: the viewer is embedded in the assembly and the graph is in memory.
/// It writes nothing.
/// </para>
/// </summary>
internal sealed class GraphServer : IAsyncDisposable
{
    private const string GraphPath = "/api/graph/";

    private readonly WebApplication _app;

    private GraphServer(WebApplication app, int port, string token)
    {
        _app = app;
        Port = port;
        Token = token;
    }

    /// <summary>The port the server listens on.</summary>
    internal int Port { get; }

    /// <summary>The per-run token that the graph address carries.</summary>
    internal string Token { get; }

    /// <summary>The address of the graph.</summary>
    internal string GraphUrl => $"http://127.0.0.1:{Port}{GraphPath}{Token}";

    /// <summary>The address that opens the viewer on the graph.</summary>
    internal string ViewerUrl => $"http://127.0.0.1:{Port}/?graph={GraphPath}{Token}";

    /// <summary>Starts the server. Port 0 takes a free port.</summary>
    internal static async Task<GraphServer> StartAsync(
        string graphJson,
        ViewerAssets viewer,
        int port,
        CancellationToken cancellationToken)
    {
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        byte[] graph = Encoding.UTF8.GetBytes(graphJson);

        // The empty builder reads no appsettings.json and no environment variables. A Kestrel section in
        // the user's working folder could otherwise add endpoints that listen on other addresses.
        WebApplicationBuilder builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.WebHost.UseKestrelCore().ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Listen(IPAddress.Loopback, port);
        });

        WebApplication app = builder.Build();
        app.Run(context => HandleAsync(context, token, graph, viewer));
        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new GraphServer(app, new Uri(address).Port, token);
    }

    /// <summary>Stops the server.</summary>
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }

    private static async Task HandleAsync(HttpContext context, string token, byte[] graph, ViewerAssets viewer)
    {
        HttpRequest request = context.Request;
        HttpResponse response = context.Response;
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers["Referrer-Policy"] = "no-referrer";

        int port = context.Connection.LocalPort;
        if (!IsThisServer(request.Host, port))
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        string origin = request.Headers.Origin.ToString();
        if (origin.Length > 0 && !IsThisOrigin(origin, port))
        {
            response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        bool head = HttpMethods.IsHead(request.Method);
        if (!head && !HttpMethods.IsGet(request.Method))
        {
            response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            response.Headers.Allow = "GET, HEAD";
            return;
        }

        string path = request.Path.Value ?? "/";
        if (path.StartsWith("/api/", StringComparison.Ordinal))
        {
            if (!HasToken(path, token))
            {
                response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            response.Headers.CacheControl = "no-store";
            await WriteAsync(response, "application/json; charset=utf-8", graph, head).ConfigureAwait(false);
            return;
        }

        if (!viewer.TryGet(path == "/" ? "index.html" : path[1..], out ViewerFile file))
        {
            response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await WriteAsync(response, file.ContentType, file.Content, head).ConfigureAwait(false);
    }

    private static async Task WriteAsync(HttpResponse response, string contentType, byte[] content, bool head)
    {
        response.ContentType = contentType;
        response.ContentLength = content.Length;
        if (!head) await response.Body.WriteAsync(content).ConfigureAwait(false);
    }

    private static bool HasToken(string path, string token)
    {
        if (!path.StartsWith(GraphPath, StringComparison.Ordinal)) return false;
        byte[] given = Encoding.ASCII.GetBytes(path[GraphPath.Length..]);
        return CryptographicOperations.FixedTimeEquals(given, Encoding.ASCII.GetBytes(token));
    }

    private static bool IsThisServer(HostString host, int port) =>
        host.Port == port &&
        (string.Equals(host.Host, "127.0.0.1", StringComparison.Ordinal) ||
         string.Equals(host.Host, "localhost", StringComparison.OrdinalIgnoreCase));

    private static bool IsThisOrigin(string origin, int port) =>
        string.Equals(origin, $"http://127.0.0.1:{port}", StringComparison.Ordinal) ||
        string.Equals(origin, $"http://localhost:{port}", StringComparison.OrdinalIgnoreCase);
}
