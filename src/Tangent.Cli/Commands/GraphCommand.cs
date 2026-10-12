using Tangent.Core.Ir;
using Tangent.Graph;

namespace Tangent.Cli.Commands;

/// <summary>
/// <c>tangent graph</c>: exports <c>tangent.graph.json</c> for the viewer. The nodes, edges and violations
/// all come from the one analysis run — the exporter is a sink the analyzer feeds during its own Tier 1
/// walk, so nothing is walked twice.
/// </summary>
public static class GraphCommand
{
    /// <summary>The default file name the exporter writes to.</summary>
    public const string DefaultFileName = "tangent.graph.json";

    /// <summary>The hosted viewer. It reads the file in the browser and never uploads it.</summary>
    public const string ViewerUrl = "https://caybokotze.github.io/tangent/";

    /// <summary>Runs the command.</summary>
    public static async Task<int> RunAsync(
        string? path,
        string? output,
        bool includeUnbound,
        bool includeSource,
        IReadOnlyList<string> projects,
        CliConsole console,
        CancellationToken cancellationToken)
    {
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

        string destination = Path.GetFullPath(output ?? Path.Combine(target.Root, DefaultFileName));
        string? directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(destination, GraphJsonWriter.Write(document));

        console.WriteLine($"Wrote {GraphBuilder.Relative(Environment.CurrentDirectory, destination)}");
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

        console.WriteLine($"Open {ViewerUrl} and choose this file. It stays in your browser.");

        return ExitCodes.Clean;
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
