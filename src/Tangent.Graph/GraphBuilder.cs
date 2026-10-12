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
    public const int SchemaVersion = 2;

    /// <summary>How many lines above and below a violation <c>--include-source</c> embeds.</summary>
    public const int SnippetContext = 15;

    /// <summary>Builds the document.</summary>
    /// <param name="model">The bound model, or null when the <c>.tan</c> files did not bind.</param>
    /// <param name="exporter">The sink that was registered on the analyzer for this run.</param>
    /// <param name="findings">Every Tangent diagnostic of the run, including TNG9000 exemption notes.</param>
    /// <param name="projectName">The graph's <c>project</c> field.</param>
    /// <param name="solutionRoot">Absolute path that file paths are made relative to.</param>
    /// <param name="generatedAt">The timestamp to stamp the document with.</param>
    /// <param name="contractFiles">Absolute paths of the <c>.tan</c> files, added to the file table.</param>
    /// <param name="includeSource">Embed violation snippets, contract text and the absolute root.</param>
    /// <param name="readFile">Reads a file by absolute path, or returns null. Defaults to the disk.</param>
    public static GraphDocument Build(
        ArchitectureModel? model,
        GraphExporter exporter,
        IReadOnlyList<GraphFinding> findings,
        string projectName,
        string solutionRoot,
        DateTimeOffset generatedAt,
        IReadOnlyList<string> contractFiles,
        bool includeSource,
        Func<string, string?>? readFile = null)
    {
        ArgumentNullException.ThrowIfNull(exporter);
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(contractFiles);

        var sources = new SourceCache(readFile ?? ReadFromDisk);
        FileTable files = BuildFiles(exporter, findings, contractFiles, solutionRoot, includeSource, sources);

        var violationsByNode = new Dictionary<string, int>(StringComparer.Ordinal);
        var exemptionsByNode = new Dictionary<string, int>(StringComparer.Ordinal);
        var violations = new List<GraphViolation>(findings.Count);
        var exemptions = new List<GraphExemption>();

        foreach (GraphFinding finding in findings)
        {
            bool isExemption = string.Equals(finding.Code, "TNG9000", StringComparison.Ordinal);
            Dictionary<string, int> counter = isExemption ? exemptionsByNode : violationsByNode;
            if (finding.NodeId.Length > 0)
            {
                counter.TryGetValue(finding.NodeId, out int current);
                counter[finding.NodeId] = current + 1;
            }

            if (isExemption)
            {
                const string prefix = "Exemption applied: ";
                string reason = finding.Message.StartsWith(prefix, StringComparison.Ordinal)
                    ? finding.Message[prefix.Length..]
                    : finding.Message;
                string exemptFile = Relative(solutionRoot, finding.File);
                exemptions.Add(new GraphExemption(
                    finding.NodeId,
                    string.IsNullOrEmpty(finding.Member) ? null : finding.Member,
                    finding.RuleId,
                    reason,
                    exemptFile,
                    finding.Line,
                    files.IdOf(exemptFile)));
                continue;
            }

            violations.Add(new GraphViolation(
                finding.NodeId,
                string.IsNullOrEmpty(finding.Member) ? null : finding.Member,
                finding.RuleId,
                finding.Code,
                finding.Message,
                Relative(solutionRoot, finding.File),
                finding.Line,
                finding.Col,
                finding.Severity,
                files.IdOf(Relative(solutionRoot, finding.File)),
                includeSource ? sources.Snippet(finding.File, finding.Line) : null));
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

        exemptions.Sort(static (a, b) =>
        {
            int byFile = string.CompareOrdinal(a.File, b.File);
            if (byFile != 0) return byFile;
            int byLine = a.Line.CompareTo(b.Line);
            return byLine != 0 ? byLine : string.CompareOrdinal(a.RuleId, b.RuleId);
        });

        List<GraphNode> nodes = BuildNodes(exporter, violationsByNode, exemptionsByNode, solutionRoot, files);
        var nodeIds = new HashSet<string>(nodes.Count, StringComparer.Ordinal);
        foreach (GraphNode node in nodes) nodeIds.Add(node.Id);

        List<GraphEdge> edges = BuildEdges(exporter, nodeIds);

        return new GraphDocument(
            SchemaVersion,
            projectName,
            generatedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            includeSource ? solutionRoot.Replace('\\', '/') : null,
            BuildLayers(model),
            BuildContracts(model, solutionRoot),
            files.Files,
            nodes,
            edges,
            violations,
            exemptions,
            BuildRules(model));
    }

    private static List<GraphNode> BuildNodes(
        GraphExporter exporter,
        Dictionary<string, int> violations,
        Dictionary<string, int> exemptions,
        string solutionRoot,
        FileTable files)
    {
        var nodes = new List<GraphNode>();
        foreach (ExportedType type in exporter.Types)
        {
            violations.TryGetValue(type.Id, out int violationCount);
            exemptions.TryGetValue(type.Id, out int exemptionCount);
            string file = Relative(solutionRoot, type.File);

            nodes.Add(new GraphNode(
                type.Id,
                type.Name,
                type.Kind,
                type.Layer,
                type.Contracts,
                file,
                type.Line,
                violationCount,
                exemptionCount,
                type.Members,
                files.IdOf(file)));
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

    private static FileTable BuildFiles(
        GraphExporter exporter,
        IReadOnlyList<GraphFinding> findings,
        IReadOnlyList<string> contractFiles,
        string solutionRoot,
        bool includeSource,
        SourceCache sources)
    {
        var bound = new HashSet<string>(StringComparer.Ordinal);
        foreach (ExportedType type in exporter.Types)
        {
            if (!type.Contracts.IsEmpty) bound.Add(type.Id);
        }

        var violations = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (GraphFinding finding in findings)
        {
            if (string.Equals(finding.Code, "TNG9000", StringComparison.Ordinal)) continue;
            string path = Relative(solutionRoot, finding.File);
            violations.TryGetValue(path, out int current);
            violations[path] = current + 1;
        }

        var entries = new List<GraphFile>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string contractFile in contractFiles)
        {
            string path = Relative(solutionRoot, contractFile);
            if (!seen.Add(path)) continue;

            string? text = sources.Text(contractFile);
            violations.TryGetValue(path, out int count);
            entries.Add(new GraphFile(
                0, path, "contract", null, text is null ? 0 : SourceCache.SplitLines(text).Length, "contract",
                Array.Empty<string>(), count, includeSource ? text : null));
        }

        foreach (ExportedFile file in exporter.Files)
        {
            string path = Relative(solutionRoot, file.Path);

            // Files outside the solution root come from packages (the test SDK's generated Program.cs, for one).
            if (Path.IsPathRooted(path)) continue;
            if (!seen.Add(path)) continue;

            int boundCount = 0;
            foreach (string id in file.Types)
            {
                if (bound.Contains(id)) boundCount++;
            }

            string status = boundCount == 0 ? "untracked" : boundCount == file.Types.Length ? "tracked" : "partial";
            violations.TryGetValue(path, out int count);
            entries.Add(new GraphFile(0, path, "csharp", file.Project, file.Lines, status, file.Types, count, null));
        }

        entries.Sort(static (a, b) => string.CompareOrdinal(a.Path, b.Path));
        for (int i = 0; i < entries.Count; i++) entries[i] = entries[i] with { Id = i };

        return new FileTable(entries);
    }

    private static string? ReadFromDisk(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The finished file table plus a path-to-id lookup.</summary>
    private sealed class FileTable
    {
        private readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal);

        internal FileTable(List<GraphFile> files)
        {
            Files = files;
            foreach (GraphFile file in files) _ids[file.Path] = file.Id;
        }

        internal List<GraphFile> Files { get; }

        internal int IdOf(string relativePath) =>
            _ids.TryGetValue(relativePath, out int id) ? id : -1;
    }

    /// <summary>Reads each file at most once and cuts snippets out of it.</summary>
    private sealed class SourceCache(Func<string, string?> read)
    {
        private readonly Dictionary<string, string?> _texts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string[]?> _lines = new(StringComparer.OrdinalIgnoreCase);

        internal string? Text(string path)
        {
            if (!_texts.TryGetValue(path, out string? text))
            {
                text = read(path);
                _texts[path] = text;
            }

            return text;
        }

        internal GraphSnippet? Snippet(string path, int line)
        {
            if (string.IsNullOrEmpty(path) || line < 1) return null;

            if (!_lines.TryGetValue(path, out string[]? lines))
            {
                string? text = Text(path);
                lines = text is null ? null : SplitLines(text);
                _lines[path] = lines;
            }

            if (lines is null || line > lines.Length) return null;

            int first = Math.Max(1, line - SnippetContext);
            int last = Math.Min(lines.Length, line + SnippetContext);
            return new GraphSnippet(first, lines[(first - 1)..last]);
        }

        internal static string[] SplitLines(string text) =>
            text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
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
