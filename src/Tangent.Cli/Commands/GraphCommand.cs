using Tangent.Cli.Serve;
using Tangent.Core.Ir;
using Tangent.Graph;

namespace Tangent.Cli.Commands;

/// <summary>
/// <c>tangent graph</c>: exports <c>tangent.graph.json</c> for the viewer. The nodes, edges and violations
/// all come from the one analysis run — the exporter is a sink the analyzer feeds during its own Tier 1
/// walk, so nothing is walked twice. With <c>--serve</c>, it serves the viewer and the graph on
/// <c>127.0.0.1</c> until Ctrl+C, and writes a file only when <c>-o</c> asks for one.
/// </summary>
public static class GraphCommand
{
    /// <summary>The default file name the exporter writes to.</summary>
    public const string DefaultFileName = "tangent.graph.json";

    /// <summary>The hosted viewer. It reads the file in the browser and never uploads it.</summary>
    public const string ViewerUrl = "https://caybokotze.github.io/tangent/";

    /// <summary>Runs the command, and serves the graph when <paramref name="serve"/> asks for it.</summary>
    /// <param name="viewer">The viewer to serve. Null uses the one this assembly embeds.</param>
    /// <param name="started">Called once the server listens; the tests use it to find the address.</param>
    internal static async Task<int> RunAsync(
        string? path,
        string? output,
        bool includeUnbound,
        bool includeSource,
        IReadOnlyList<string> projects,
        ServeOptions serve,
        CliConsole console,
        CancellationToken cancellationToken,
        ViewerAssets? viewer = null,
        Action<GraphServer>? started = null)
    {
        if (serve.Enabled)
        {
            viewer ??= ViewerAssets.FromAssembly();
            if (!viewer.HasViewer)
            {
                console.WriteError("error: this build of tangent has no viewer. Run 'npm ci' and 'npm run build' in viewer/, then build the CLI again.");
                return ExitCodes.LoadFailure;
            }
        }

        Target? target = TargetLocator.Resolve(path, out string? error);
        if (target is null)
        {
            console.WriteError("error: " + error);
            return ExitCodes.LoadFailure;
        }

        var exporter = new GraphExporter();
        using AnalysisResult analysis = await AnalysisRunner
            .RunAsync(target, projects, exporter, includeUnbound, console, cancellationToken)
            .ConfigureAwait(false);

        GraphDocument document = Build(analysis, exporter, DateTimeOffset.UtcNow, includeSource);
        string json = GraphJsonWriter.Write(document);

        if (!serve.Enabled || output is not null)
        {
            string destination = Path.GetFullPath(output ?? Path.Combine(target.Root, DefaultFileName));
            string? directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(destination, json);
            console.WriteLine($"Wrote {GraphBuilder.Relative(Environment.CurrentDirectory, destination)}");
        }

        WriteSummary(document, includeUnbound, console);

        if (!serve.Enabled)
        {
            console.WriteLine($"Open {ViewerUrl} and choose this file. It stays in your browser.");
            return ExitCodes.Clean;
        }

        return await ServeAsync(json, viewer!, serve, console, started, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> ServeAsync(
        string json,
        ViewerAssets viewer,
        ServeOptions serve,
        CliConsole console,
        Action<GraphServer>? started,
        CancellationToken cancellationToken)
    {
        await using GraphServer server = await GraphServer
            .StartAsync(json, viewer, serve.Port, cancellationToken)
            .ConfigureAwait(false);

        console.WriteLine($"Serving the viewer at {server.ViewerUrl}");
        console.WriteLine("  Only this computer can reach it. Press Ctrl+C to stop.");

        if (serve.Open)
        {
            string? problem = Browser.Open(server.ViewerUrl);
            if (problem is not null) console.WriteError($"warning: could not start a browser ({problem}). Open the address yourself.");
        }

        started?.Invoke(server);

        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C is how the server stops.
        }

        console.WriteLine("Stopped.");
        return ExitCodes.Clean;
    }

    private static void WriteSummary(GraphDocument document, bool includeUnbound, CliConsole console)
    {
        console.WriteLine(
            $"  {document.Layers.Count} layers, {document.Contracts.Count} contracts, " +
            $"{document.Nodes.Count} nodes, {document.Edges.Count} edges, {document.Violations.Count} violations");

        int untracked = document.Files.Count(f => f.Status == "untracked");
        int partial = document.Files.Count(f => f.Status == "partial");
        int tracked = document.Files.Count(f => f.Status == "tracked");
        console.WriteLine($"  {tracked + partial + untracked} C# files: {tracked} tracked, {partial} partial, {untracked} untracked");

        if (!includeUnbound)
        {
            console.WriteLine("  (only types bound to a contract are included; pass --include-unbound for the whole solution)");
        }
    }

    /// <summary>Builds the document from a finished analysis run.</summary>
    internal static GraphDocument Build(AnalysisResult analysis, GraphExporter exporter, DateTimeOffset generatedAt, bool includeSource)
    {
        ArchitectureModel? model = analysis.Model.Model;

        var findings = new List<GraphFinding>(analysis.Findings.Count);
        foreach (Finding finding in analysis.Findings)
        {
            if (finding.Severity == "hidden" && !finding.IsIgnored) continue;

            findings.Add(new GraphFinding(
                finding.NodeId,
                finding.Member,
                finding.RuleId,
                finding.Code,
                finding.Message,
                finding.File,
                finding.Line,
                finding.Col,
                finding.Severity));
        }

        string projectName = model is not null && model.ProjectName.Value >= 0 && model.Text(model.ProjectName).Length > 0
            ? model.Text(model.ProjectName)
            : Path.GetFileNameWithoutExtension(analysis.Target.Path);

        return GraphBuilder.Build(
            model, exporter, findings, projectName, analysis.Target.Root, generatedAt,
            analysis.Target.ContractFiles, includeSource);
    }
}

/// <summary>The <c>--serve</c>, <c>--open</c> and <c>--port</c> options of <c>tangent graph</c>.</summary>
/// <param name="Serve">Serve the viewer and the graph.</param>
/// <param name="Open">Also open the viewer in the default browser. Implies <paramref name="Serve"/>.</param>
/// <param name="Port">The port to listen on; 0 takes a free one.</param>
internal sealed record ServeOptions(bool Serve, bool Open, int Port)
{
    /// <summary>True when the command serves the graph.</summary>
    internal bool Enabled => Serve || Open;
}
