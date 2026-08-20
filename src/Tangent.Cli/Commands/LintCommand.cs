using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Tangent.Core;
using Tangent.Core.Binding;
using Tangent.Core.Diagnostics;
using Tangent.Core.Ir;
using Tangent.Core.Matching;
using Tangent.Core.Serialization;
using Tangent.Core.Syntax;
using Tangent.Core.Text;
using Tangent.Graph;

namespace Tangent.Cli.Commands;

/// <summary>
/// <c>tangent lint</c>: everything that can be said about the <c>.tan</c> files themselves. It parses and
/// binds them with no compilation at all, which is fast; when a solution is in reach it additionally
/// resolves the C# type names the contracts mention and reports TNG1020 for the ones that exist nowhere.
/// </summary>
public static class LintCommand
{
    /// <summary>Runs the command.</summary>
    public static async Task<int> RunAsync(
        string? path,
        bool strict,
        bool dumpIr,
        bool dumpAst,
        CliConsole console,
        CancellationToken cancellationToken)
    {
        Target? target = TargetLocator.Resolve(path, out string? resolveError);
        string? folder = target?.TangentFolder ?? FolderFor(path);

        if (folder is null)
        {
            console.WriteError("error: " + (resolveError ?? $"no {TargetLocator.FolderName} folder was found."));
            return ExitCodes.LoadFailure;
        }

        IReadOnlyList<string> contractFiles = TargetLocator.EnumerateContracts(folder);
        if (contractFiles.Count == 0)
        {
            console.WriteError($"error: '{folder}' contains no .tan files.");
            return ExitCodes.LoadFailure;
        }

        var sources = new SourceFile[contractFiles.Count];
        for (int i = 0; i < contractFiles.Count; i++)
        {
            sources[i] = new SourceFile(contractFiles[i], File.ReadAllText(contractFiles[i]));
        }

        BindResult result = ModelLoader.Load(sources);
        string root = target?.Root ?? Path.GetDirectoryName(folder) ?? folder;

        if (dumpAst)
        {
            foreach (Tangent.Core.Syntax.SyntaxTree tree in ModelLoader.ParseAll(sources))
            {
                console.WriteLine("=== " + GraphBuilder.Relative(root, tree.Source.Path) + " ===");
                console.WriteLine(SyntaxTreePrinter.Print(tree));
            }
        }

        if (dumpIr)
        {
            console.WriteLine(IrJsonWriter.Write(result.Model));
        }

        var diagnostics = new List<TangentDiagnostic>(result.Diagnostics);

        if (target is not null && result.Model is not null)
        {
            diagnostics.AddRange(await ResolveTypeNamesAsync(target, result.Model, console, cancellationToken).ConfigureAwait(false));
        }
        else
        {
            console.WriteLine("note: no solution or project was found, so contract type names were not resolved (TNG1020 skipped).");
        }

        int errors = 0;
        int warnings = 0;

        foreach (TangentDiagnostic diagnostic in diagnostics
                     .OrderBy(d => d.FilePath, StringComparer.Ordinal)
                     .ThenBy(d => d.Span.Line)
                     .ThenBy(d => d.Span.Column)
                     .ThenBy(d => d.Code, StringComparer.Ordinal))
        {
            Core.Diagnostics.DiagnosticSeverity severity = diagnostic.Severity;
            if (strict && severity == Core.Diagnostics.DiagnosticSeverity.Warning)
            {
                severity = Core.Diagnostics.DiagnosticSeverity.Error;
            }

            if (severity == Core.Diagnostics.DiagnosticSeverity.Error) errors++;
            else if (severity == Core.Diagnostics.DiagnosticSeverity.Warning) warnings++;

            string name = severity.ToString().ToLowerInvariant();
            string location = GraphBuilder.Relative(root, diagnostic.FilePath ?? string.Empty) +
                              ":" + diagnostic.Span.Line.ToString(CultureInfo.InvariantCulture) +
                              ":" + diagnostic.Span.Column.ToString(CultureInfo.InvariantCulture);

            console.WriteLine(
                "  " + console.Paint(name, severity == Core.Diagnostics.DiagnosticSeverity.Error ? AnsiColor.Red : AnsiColor.Yellow) +
                " " + console.Paint(diagnostic.Code, AnsiColor.Bold) +
                " " + console.Paint(location, AnsiColor.Grey) +
                "  " + diagnostic.Message);
        }

        ArchitectureModel? model = result.Model;
        if (model is not null)
        {
            console.WriteLine();
            console.WriteLine(
                $"{model.Contracts.Length} {(model.Contracts.Length == 1 ? "contract" : "contracts")}, " +
                $"{model.Layers.Length} {(model.Layers.Length == 1 ? "layer" : "layers")}, " +
                $"{contractFiles.Count} {(contractFiles.Count == 1 ? "file" : "files")} — " +
                $"{errors} {(errors == 1 ? "error" : "errors")}, {warnings} {(warnings == 1 ? "warning" : "warnings")}");
        }

        return errors > 0 ? ExitCodes.ModelError : ExitCodes.Clean;
    }

    private static string? FolderFor(string? path)
    {
        string start = string.IsNullOrWhiteSpace(path) ? Environment.CurrentDirectory : Path.GetFullPath(path!);
        if (File.Exists(start)) start = Path.GetDirectoryName(start) ?? start;
        return Directory.Exists(start) ? TargetLocator.FindTangentFolder(start) : null;
    }

    /// <summary>
    /// TNG1020: a contract names a C# type that no compilation in the solution knows about. This is the
    /// CLI's job rather than the analyzer's, because only the CLI sees every project at once.
    /// </summary>
    private static async Task<List<TangentDiagnostic>> ResolveTypeNamesAsync(
        Target target,
        ArchitectureModel model,
        CliConsole console,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<TangentDiagnostic>();

        AnalysisRunner.EnsureMSBuild();
        using MSBuildWorkspace workspace = MSBuildWorkspace.Create();
        workspace.RegisterWorkspaceFailedHandler(e => console.Trace("msbuild: " + e.Diagnostic.Message));

        if (target.IsSolution)
        {
            await workspace.OpenSolutionAsync(target.Path, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await workspace.OpenProjectAsync(target.Path, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        var compilations = new List<Compilation>();
        var simpleNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (Project project in workspace.CurrentSolution.Projects)
        {
            if (project.Language != LanguageNames.CSharp) continue;
            Compilation? compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null) continue;

            compilations.Add(compilation);
            foreach (string name in compilation.Assembly.TypeNames) simpleNames.Add(name);
            foreach (IAssemblySymbol referenced in compilation.SourceModule.ReferencedAssemblySymbols)
            {
                foreach (string name in referenced.TypeNames) simpleNames.Add(name);
            }
        }

        if (compilations.Count == 0)
        {
            console.WriteLine("note: the solution produced no compilations, so contract type names were not resolved (TNG1020 skipped).");
            return diagnostics;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int c = 0; c < model.Contracts.Length; c++)
        {
            Contract contract = model.Contracts[c];
            string contractName = model.Text(contract.Name);

            foreach (Rule rule in contract.Rules)
            {
                if (rule.OriginContract != c) continue;
                if (!UsesTypePatterns(rule.Predicate.Kind)) continue;

                foreach (int patternIndex in Flatten(model, rule.Predicate.Payload))
                {
                    TypePattern pattern = model.TypePatterns.Patterns[patternIndex];
                    if (pattern.IsWildcard || pattern.TemplateIndex >= 0) continue;

                    string name = model.Text(pattern.Name);
                    if (name.Length == 0 || IsPrimitive(name)) continue;
                    if (!seen.Add(contractName + "/" + name)) continue;
                    if (Exists(compilations, simpleNames, name, pattern.Arity)) continue;

                    diagnostics.Add(TangentDiagnostic.Create(
                        DiagnosticCatalog.UnresolvedTypeName,
                        rule.Span,
                        model.FilePathOf(rule.Span),
                        name,
                        contractName));
                }
            }
        }

        return diagnostics;
    }

    private static IEnumerable<int> Flatten(ArchitectureModel model, int groupIndex)
    {
        if (groupIndex < 0 || groupIndex >= model.TypePatterns.Groups.Length) yield break;

        var pending = new Stack<int>();
        foreach (int index in model.TypePatterns.Group(groupIndex)) pending.Push(index);

        while (pending.Count > 0)
        {
            int index = pending.Pop();
            TypePattern pattern = model.TypePatterns.Patterns[index];
            for (int i = 0; i < pattern.Arity; i++) pending.Push(model.TypePatterns.Argument(pattern, i));
            yield return index;
        }
    }

    private static bool Exists(List<Compilation> compilations, HashSet<string> simpleNames, string name, int arity)
    {
        if (name.IndexOf('.') < 0)
        {
            return simpleNames.Contains(name) ||
                   (arity > 0 && simpleNames.Contains(name + "`" + arity.ToString(CultureInfo.InvariantCulture)));
        }

        foreach (Compilation compilation in compilations)
        {
            if (compilation.GetTypeByMetadataName(name) is not null) return true;
            if (arity > 0 && compilation.GetTypeByMetadataName(name + "`" + arity.ToString(CultureInfo.InvariantCulture)) is not null) return true;
        }

        // A dotted pattern is a namespace-qualified suffix, so fall back to its simple name.
        string simple = name[(name.LastIndexOf('.') + 1)..];
        return simpleNames.Contains(simple) ||
               (arity > 0 && simpleNames.Contains(simple + "`" + arity.ToString(CultureInfo.InvariantCulture)));
    }

    private static bool UsesTypePatterns(PredicateKind kind) => kind is
        PredicateKind.Inherits or
        PredicateKind.Implements or
        PredicateKind.Returns or
        PredicateKind.Injects or
        PredicateKind.InjectsOnly or
        PredicateKind.NewExpression;

    private static readonly ImmutableHashSet<string> Primitives = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "void", "object", "string", "bool", "byte", "sbyte", "char", "short", "ushort",
        "int", "uint", "long", "ulong", "float", "double", "decimal", "nint", "nuint", "dynamic");

    private static bool IsPrimitive(string name) => Primitives.Contains(name);
}
