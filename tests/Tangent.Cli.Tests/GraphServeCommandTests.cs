using Tangent.Cli.Commands;
using Tangent.Cli.Serve;
using Xunit;

namespace Tangent.Cli.Tests;

/// <summary><c>tangent graph --serve</c> end to end, over <c>samples/Orders</c>.</summary>
[Collection(OrdersCollection.Name)]
public sealed class GraphServeCommandTests
{
    [Fact]
    public async Task ServesTheGraphUntilCancelledAndWritesNoFile()
    {
        string file = Path.Combine(Cli.OrdersSample, GraphCommand.DefaultFileName);
        DateTime writtenBefore = File.GetLastWriteTimeUtc(file);

        using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var stdout = new StringWriter();
        var console = new CliConsole(stdout, new StringWriter(), color: false, verbose: false);
        var listening = new TaskCompletionSource<GraphServer>(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<int> run = GraphCommand.RunAsync(
            Cli.OrdersSample, output: null, includeUnbound: false, includeSource: false, projects: [],
            new ServeOptions(Serve: true, Open: false, Port: 0), console, cancel.Token,
            GraphServerTests.Viewer, listening.SetResult);

        GraphServer server = await listening.Task.WaitAsync(cancel.Token);
        using var http = new HttpClient();
        string json = await http.GetStringAsync(server.GraphUrl, cancel.Token);

        await cancel.CancelAsync();
        int exit = await run;

        Assert.Equal(ExitCodes.Clean, exit);
        Assert.Contains("\"project\": \"Orders\"", json, StringComparison.Ordinal);
        Assert.Contains($"Serving the viewer at {server.ViewerUrl}", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("Stopped.", stdout.ToString(), StringComparison.Ordinal);
        Assert.Equal(writtenBefore, File.GetLastWriteTimeUtc(file));
    }

    [Fact]
    public async Task ABuildWithoutTheViewerSaysHowToAddIt()
    {
        var stderr = new StringWriter();
        var console = new CliConsole(new StringWriter(), stderr, color: false, verbose: false);

        int exit = await GraphCommand.RunAsync(
            Cli.OrdersSample, output: null, includeUnbound: false, includeSource: false, projects: [],
            new ServeOptions(Serve: false, Open: true, Port: 0), console, CancellationToken.None,
            new ViewerAssets([]));

        Assert.Equal(ExitCodes.LoadFailure, exit);
        Assert.Contains("npm run build", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAPortOutOfRange()
    {
        CliRun run = Cli.Run("graph", Cli.OrdersSample, "--serve", "--port", "70000", "--no-color");

        Assert.NotEqual(ExitCodes.Clean, run.ExitCode);
        Assert.Contains("--port must be between 0 and 65535.", run.Error, StringComparison.Ordinal);
    }
}
