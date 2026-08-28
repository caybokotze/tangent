using System.Collections.Immutable;
using System.Globalization;
using Tangent.Core.Ir;

namespace Tangent.Graph;

/// <summary>
/// Turns a bound <see cref="ArchitectureModel"/>, the analyzer's reference sink and the diagnostics of a
/// run into the <c>tangent.graph.json</c> document described by TANGENT-PLAN.md §11.1.
/// Everything is ordered deterministically so two runs over unchanged sources produce byte-identical output.
/// </summary>
public static class GraphBuilder
{
    /// <summary>The schema version this builder writes.</summary>
    public const int SchemaVersion = 1;

    /// <summary>Builds the document.</summary>
    /// <param name="model">The bound model, or null when the <c>.tan</c> files did not bind.</param>
    /// <param name="exporter">The sink that was registered on the analyzer for this run.</param>
    /// <param name="findings">Every Tangent diagnostic of the run, including TNG9000 exemption notes.</param>
    /// <param name="projectName">The graph's <c>project</c> field.</param>
    /// <param name="solutionRoot">Absolute path that file paths are made relative to.</param>
    /// <param name="generatedAt">The timestamp to stamp the document with.</param>
    public static GraphDocument Build(
        ArchitectureModel? model,
        GraphExporter exporter,
        IReadOnlyList<GraphFinding> findings,
        string projectName,
        string solutionRoot,
        DateTimeOffset generatedAt)
    {
        ArgumentNullException.ThrowIfNull(exporter);
        ArgumentNullException.ThrowIfNull(findings);

        var violationsByNode = new Dictionary<string, int>(StringComparer.Ordinal);
        var exemptionsByNode = new Dictionary<string, int>(StringComparer.Ordinal);
        var violations = new List<GraphViolation>(findings.Count);

        foreach (GraphFinding finding in findings)
        {
            bool isExemption = string.Equals(finding.Code, "TNG9000", StringComparison.Ordinal);
            Dictionary<string, int> counter = isExemption ? exemptionsByNode : violationsByNode;
            if (finding.NodeId.Length > 0)
            {
                counter.TryGetValue(finding.NodeId, out int current);
                counter[finding.NodeId] = current + 1;
            }

            if (isExemption) continue;

            violations.Add(new GraphViolation(
                finding.NodeId,
                string.IsNullOrEmpty(finding.Member) ? null : finding.Member,
                finding.RuleId,
                finding.Code,
                finding.Message,
                Relative(solutionRoot, finding.File),
                finding.Line,
                finding.Col,
                finding.Severity));
        }

        violations.Sort(static (a, b) =>
        {
            int byFile = string.CompareOrdinal(a.File, b.File);
            if (byFile != 0) return byFile;
            int byLine = a.Line.CompareTo(b.Line);
            if (byLine != 0) return byLine;
            int byCol = a.Col.CompareTo(b.Col);
            if (byCol != 0) return byCol;
            int byCode = string.CompareOrdinal(a.Code, b.Code);
            return byCode != 0 ? byCode : string.CompareOrdinal(a.Message, b.Message);
        });

        List<GraphNode> nodes = BuildNodes(exporter, violationsByNode, exemptionsByNode, solutionRoot);
        var nodeIds = new HashSet<string>(nodes.Count, StringComparer.Ordinal);
        foreach (GraphNode node in nodes) nodeIds.Add(node.Id);

        List<GraphEdge> edges = BuildEdges(exporter, nodeIds);

        return new GraphDocument(
            SchemaVersion,
            projectName,
            generatedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            BuildLayers(model),
            BuildContracts(model, solutionRoot),
            nodes,
            edges,
            violations,
            BuildRules(model));
    }

    private static List<GraphNode> BuildNodes(
        GraphExporter exporter,
        Dictionary<string, int> violations,
        Dictionary<string, int> exemptions,
        string solutionRoot)
    {
        var nodes = new List<GraphNode>();
        foreach (ExportedType type in exporter.Types)
        {
            violations.TryGetValue(type.Id, out int violationCount);
            exemptions.TryGetValue(type.Id, out int exemptionCount);

            nodes.Add(new GraphNode(
                type.Id,
                type.Name,
                type.Kind,
                type.Layer,
                type.Contracts,
                Relative(solutionRoot, type.File),
                type.Line,
                violationCount,
                exemptionCount,
                type.Members));
        }

        nodes.Sort(static (a, b) => string.CompareOrdinal(a.Id, b.Id));
        return nodes;
    }

    private static List<GraphEdge> BuildEdges(GraphExporter exporter, HashSet<string> nodeIds)
    {
        var edges = new List<GraphEdge>();
        foreach (ExportedReference reference in exporter.References)
        {
            // Edges to types outside the graph (the BCL, NuGet packages, and — without --include-unbound —
            // types no contract binds) would render as dangling. Drop them.
            if (!nodeIds.Contains(reference.From) || !nodeIds.Contains(reference.To)) continue;

            edges.Add(new GraphEdge(reference.From, reference.To, "reference", reference.Count, reference.ViolatesLayering));
        }

        edges.Sort(static (a, b) =>
        {
            int byFrom = string.CompareOrdinal(a.From, b.From);
            return byFrom != 0 ? byFrom : string.CompareOrdinal(a.To, b.To);
        });

        return edges;
    }

    private static List<GraphLayer> BuildLayers(ArchitectureModel? model)
    {
        var layers = new List<GraphLayer>();
        if (model is null) return layers;

        for (int i = 0; i < model.Layers.Length; i++)
        {
            Layer layer = model.Layers[i];
            var allowed = new List<string>();
            foreach (int target in model.Dependencies.AllowedTargets(i))
            {
                if (target == i) continue;                       // self is implicit
                if (target < 0 || target >= model.Layers.Length) continue;
                allowed.Add(model.Text(model.Layers[target].Name));
            }

            layers.Add(new GraphLayer(
                model.Text(layer.Name),
                new[] { model.Globs[layer.GlobIndex].Text },
                allowed));
        }

        return layers;
    }

    private static List<GraphContract> BuildContracts(ArchitectureModel? model, string solutionRoot)
    {
        var contracts = new List<GraphContract>();
        if (model is null) return contracts;

        for (int i = 0; i < model.Contracts.Length; i++)
        {
            Contract contract = model.Contracts[i];

            int own = 0;
            foreach (Rule rule in contract.Rules)
            {
                if (rule.OriginContract == i) own++;
            }

            contracts.Add(new GraphContract(
                model.Text(contract.Name),
                Relative(solutionRoot, model.FilePathOf(contract.DeclarationSpan) ?? string.Empty),
                contract.DeclarationSpan.Line,
                contract.ExtendsIndex >= 0 ? model.Text(model.Contracts[contract.ExtendsIndex].Name) : null,
                own));
        }

        return contracts;
    }

    private static List<GraphRule>? BuildRules(ArchitectureModel? model)
    {
        if (model is null) return null;

        var rules = new List<GraphRule>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < model.Contracts.Length; i++)
        {
            Contract contract = model.Contracts[i];
            ImmutableArray<Rule> contractRules = contract.Rules;
            for (int r = 0; r < contractRules.Length; r++)
            {
                Rule rule = contractRules[r];
                if (rule.OriginContract != i) continue;          // inherited rules keep the parent's id
                if (!seen.Add(rule.Id.Value)) continue;

                rules.Add(new GraphRule(
                    rule.Id.Value,
                    model.Text(contract.Name),
                    IrText.DescribeRule(model, rule),
                    (int)rule.Tier));
            }
        }

        return rules;
    }

    /// <summary>Makes <paramref name="path"/> relative to <paramref name="root"/>, with forward slashes.</summary>
    public static string Relative(string root, string path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        if (string.IsNullOrEmpty(root)) return path.Replace('\\', '/');

        try
        {
            string relative = Path.GetRelativePath(root, path);
            if (relative.StartsWith("..", StringComparison.Ordinal)) return path.Replace('\\', '/');
            return relative.Replace('\\', '/');
        }
        catch (ArgumentException)
        {
            return path.Replace('\\', '/');
        }
    }
}
