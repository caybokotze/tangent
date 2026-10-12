using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using Tangent.Analyzers;
using Tangent.Core.Binding;
using Tangent.Graph;

namespace Tangent.Cli;

/// <summary>What one analysis run produced. Disposing it disposes the workspace.</summary>
public sealed class AnalysisResult : IDisposable
{
    internal AnalysisResult(
        MSBuildWorkspace workspace,
        Solution originalSolution,
        Solution analysisSolution,
        Target target,
        BindResult model,
        IReadOnlyList<Finding> findings,
        IReadOnlyList<string> projects,
        int compilerErrors,
        GraphExporter? exporter)
    {
        Workspace = workspace;
        OriginalSolution = originalSolution;
        AnalysisSolution = analysisSolution;
        Target = target;
        Model = model;
        Findings = findings;
        Projects = projects;
        CompilerErrors = compilerErrors;
        Exporter = exporter;
    }

    /// <summary>The loaded workspace. <c>tangent diff --apply</c> writes through it.</summary>
    public MSBuildWorkspace Workspace { get; }

    /// <summary>The solution exactly as it was loaded, with no <c>.tan</c> files attached.</summary>
    public Solution OriginalSolution { get; }

    /// <summary>The same solution with every <c>.tan</c> file added to every C# project as an additional document.</summary>
    public Solution AnalysisSolution { get; }

    /// <summary>What the <c>path</c> argument resolved to.</summary>
    public Target Target { get; }

    /// <summary>The bound <c>.tan</c> model, loaded once through <see cref="ContractFiles.LoadModel"/>.</summary>
    public BindResult Model { get; }

    /// <summary>Every TNG diagnostic, deduplicated across projects and sorted.</summary>
    public IReadOnlyList<Finding> Findings { get; }

    /// <summary>The names of the projects that were analysed.</summary>
    public IReadOnlyList<string> Projects { get; }

    /// <summary>How many C# compiler errors the analysed compilations had.</summary>
    public int CompilerErrors { get; }

    /// <summary>The graph sink, when one was registered.</summary>
    public GraphExporter? Exporter { get; }

    /// <inheritdoc/>
    public void Dispose() => Workspace.Dispose();
}

/// <summary>
/// Loads the solution through MSBuild and runs <see cref="TangentAnalyzer"/> over every project, once.
/// <c>check</c>, <c>graph</c> and <c>diff</c> all go through here so a run costs one workspace load and
/// one analyzer pass no matter how many outputs are asked for.
/// </summary>
public static class AnalysisRunner
{
    private static readonly object LocatorGate = new();

    /// <summary>Registers the MSBuild toolset, once per process.</summary>
    public static void EnsureMSBuild()
    {
        lock (LocatorGate)
        {
            if (!MSBuildLocator.IsRegistered) MSBuildLocator.RegisterDefaults();
        }
    }

    /// <summary>Loads the target and analyses it.</summary>
    /// <param name="target">The resolved solution or project.</param>
    /// <param name="projectFilter">Project names to restrict the run to; empty means every project.</param>
    /// <param name="exporter">A graph sink to register on the analyzer, or null.</param>
    /// <param name="includeUnbound">Passed to the analyzer so unbound types reach the sink too.</param>
    /// <param name="console">Where load failures are traced.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <exception cref="AnalysisLoadException">The workspace or the <c>.tangent</c> folder could not be loaded.</exception>
    public static async Task<AnalysisResult> RunAsync(
        Target target,
        IReadOnlyList<string> projectFilter,
        GraphExporter? exporter,
        bool includeUnbound,
        CliConsole console,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (target.TangentFolder is null)
        {
            throw new AnalysisLoadException(
                $"No .tangent folder was found at '{target.Root}' or in any ancestor directory. Run 'tangent init' to create one.");
        }

        if (target.ContractFiles.Count == 0)
        {
            throw new AnalysisLoadException($"'{target.TangentFolder}' contains no .tan files.");
        }

        EnsureMSBuild();

        MSBuildWorkspace workspace = MSBuildWorkspace.Create();
        try
        {
            workspace.RegisterWorkspaceFailedHandler(e => console.Trace("msbuild: " + e.Diagnostic.Message));

            if (target.IsSolution)
            {
                await workspace.OpenSolutionAsync(target.Path, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await workspace.OpenProjectAsync(target.Path, cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            Solution original = workspace.CurrentSolution;
            if (!original.Projects.Any())
            {
                throw new AnalysisLoadException($"'{target.Path}' produced no projects. Run with --verbose to see the MSBuild diagnostics.");
            }

            Solution analysisSolution = AttachContracts(original, target.ContractFiles);

            var selected = new List<Project>();
            foreach (Project project in analysisSolution.Projects)
            {
                if (project.Language != LanguageNames.CSharp) continue;
                if (projectFilter.Count > 0 &&
                    !projectFilter.Contains(project.Name, StringComparer.OrdinalIgnoreCase)) continue;
                selected.Add(project);
            }

            if (selected.Count == 0)
            {
                string names = string.Join(", ", analysisSolution.Projects.Select(p => p.Name));
                throw new AnalysisLoadException(
                    projectFilter.Count > 0
                        ? $"--project matched nothing. The solution has: {names}."
                        : $"'{target.Path}' contains no C# projects.");
            }

            // One analyzer instance for the whole solution: per-compilation state is created in
            // CompilationStartAction and never shared, and this is what guarantees the CLI and the IDE agree.
            var analyzer = new TangentAnalyzer(exporter, includeUnbound);

            var collected = new ConcurrentBag<Finding>();
            int compilerErrors = 0;

            await Parallel.ForEachAsync(
                selected,
                new ParallelOptions { CancellationToken = cancellationToken },
                async (project, ct) =>
                {
                    Compilation? compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
                    if (compilation is null) return;

                    foreach (Diagnostic diagnostic in compilation.GetDiagnostics(ct))
                    {
                        if (diagnostic.Severity != DiagnosticSeverity.Error) continue;
                        Interlocked.Increment(ref compilerErrors);
                        console.Trace($"{project.Name}: {diagnostic.Id}: {diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}");
                    }

                    exporter?.AddSourceFiles(compilation, project.Name, ct);

                    CompilationWithAnalyzers withAnalyzers = compilation.WithAnalyzers(
                        ImmutableArray.Create<DiagnosticAnalyzer>(analyzer),
                        project.AnalyzerOptions);

                    ImmutableArray<Diagnostic> diagnostics =
                        await withAnalyzers.GetAnalyzerDiagnosticsAsync(ct).ConfigureAwait(false);

                    var typeIds = new TypeIdResolver(compilation);
                    foreach (Diagnostic diagnostic in diagnostics)
                    {
                        if (!diagnostic.Id.StartsWith("TNG", StringComparison.Ordinal)) continue;

                        DocumentId? documentId = DocumentOf(project, diagnostic);
                        collected.Add(new Finding(diagnostic, project.Name, documentId, typeIds.Resolve(diagnostic, ct)));
                    }
                }).ConfigureAwait(false);

            IReadOnlyList<Finding> findings = Deduplicate(collected);

            BindResult model = LoadModel(target.ContractFiles);

            var projectNames = selected.Select(p => p.Name).ToList();
            projectNames.Sort(StringComparer.Ordinal);

            return new AnalysisResult(
                workspace, original, analysisSolution, target, model, findings, projectNames, compilerErrors, exporter);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    /// <summary>Loads the <c>.tan</c> model on its own, without a compilation. Used by <c>tangent lint</c>.</summary>
    public static BindResult LoadModel(IReadOnlyList<string> contractFiles)
    {
        ImmutableArray<AdditionalText>.Builder builder = ImmutableArray.CreateBuilder<AdditionalText>(contractFiles.Count);
        foreach (string path in contractFiles) builder.Add(new FileAdditionalText(path));
        return ContractFiles.LoadModel(builder.MoveToImmutable());
    }

    private static Solution AttachContracts(Solution solution, IReadOnlyList<string> contractFiles)
    {
        var texts = new List<(string Name, string Path, SourceText Text)>(contractFiles.Count);
        foreach (string path in contractFiles)
        {
            texts.Add((Path.GetFileName(path), path, SourceText.From(File.ReadAllText(path))));
        }

        foreach (ProjectId projectId in solution.ProjectIds.ToArray())
        {
            Project? project = solution.GetProject(projectId);
            if (project is null || project.Language != LanguageNames.CSharp) continue;

            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (TextDocument existing in project.AdditionalDocuments)
            {
                if (existing.FilePath is not null) present.Add(existing.FilePath);
            }

            foreach ((string name, string path, SourceText text) in texts)
            {
                if (present.Contains(path)) continue;
                project = solution.GetProject(projectId)!;
                solution = project.AddAdditionalDocument(name, text, filePath: path).Project.Solution;
            }
        }

        return solution;
    }

    private static DocumentId? DocumentOf(Project project, Diagnostic diagnostic)
    {
        SyntaxTree? tree = diagnostic.Location.SourceTree;
        return tree is null ? null : project.GetDocumentId(tree);
    }

    private static IReadOnlyList<Finding> Deduplicate(IEnumerable<Finding> findings)
    {
        // Shared source files, linked files and multi-targeting make the same diagnostic arrive once per
        // project. Key on everything a reader can see so genuinely distinct findings survive.
        var seen = new HashSet<(string, string, int, int, int, int, string)>();
        var result = new List<Finding>();

        foreach (Finding finding in findings)
        {
            if (!seen.Add((finding.Code, finding.File, finding.Line, finding.Col, finding.EndLine, finding.EndCol, finding.Message))) continue;
            result.Add(finding);
        }

        result.Sort(CompareFindings);
        return result;
    }

    private static int CompareFindings(Finding a, Finding b)
    {
        int byFile = string.CompareOrdinal(a.File, b.File);
        if (byFile != 0) return byFile;
        int byLine = a.Line.CompareTo(b.Line);
        if (byLine != 0) return byLine;
        int byCol = a.Col.CompareTo(b.Col);
        if (byCol != 0) return byCol;
        int byCode = string.CompareOrdinal(a.Code, b.Code);
        return byCode != 0 ? byCode : string.CompareOrdinal(a.Message, b.Message);
    }

    /// <summary>
    /// Maps a diagnostic back to the type it was reported inside, so the graph can hang it on a node.
    /// Syntax trees are cached per compilation because several diagnostics usually share a file.
    /// </summary>
    private sealed class TypeIdResolver(Compilation compilation)
    {
        private readonly Dictionary<SyntaxTree, SyntaxNode> _roots = [];

        internal string Resolve(Diagnostic diagnostic, CancellationToken cancellationToken)
        {
            SyntaxTree? tree = diagnostic.Location.SourceTree;
            if (tree is null || !diagnostic.Location.IsInSource) return string.Empty;

            if (!_roots.TryGetValue(tree, out SyntaxNode? root))
            {
                root = tree.GetRoot(cancellationToken);
                _roots[tree] = root;
            }

            if (diagnostic.Location.SourceSpan.End > root.FullSpan.End) return string.Empty;

            SyntaxNode? node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            while (node is not null and not BaseTypeDeclarationSyntax) node = node.Parent;
            if (node is null) return string.Empty;

            SemanticModel semanticModel = compilation.GetSemanticModel(tree);
            return semanticModel.GetDeclaredSymbol(node, cancellationToken) is INamedTypeSymbol type
                ? GraphExporter.IdOf(type)
                : string.Empty;
        }
    }
}

/// <summary>Thrown when the solution, project or <c>.tangent</c> folder could not be loaded (exit code 3).</summary>
public sealed class AnalysisLoadException : Exception
{
    /// <summary>Creates the exception.</summary>
    public AnalysisLoadException(string message) : base(message) { }
}

/// <summary>An <see cref="AdditionalText"/> over a file on disk, for running the analyzer outside MSBuild.</summary>
internal sealed class FileAdditionalText(string path) : AdditionalText
{
    private readonly Lazy<SourceText> _text = new(() => SourceText.From(File.ReadAllText(path)));

    public override string Path { get; } = path;

    public override SourceText GetText(CancellationToken cancellationToken = default) => _text.Value;
}
