using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Tangent.Core.Diagnostics;
using Tangent.Core.Syntax;
using Tangent.Core.Text;
using Tangent.LanguageServer.Roslyn;

namespace Tangent.LanguageServer.Workspace;

/// <summary>
/// Reports TNG1020 for type patterns that no type in the compilation can satisfy. It runs over the
/// syntax trees rather than the IR because only the trees still know where each pattern was written.
/// Without a compilation it reports nothing, which is exactly the behaviour the plan asks for.
/// </summary>
internal static class UnresolvedTypeAnalyzer
{
    /// <summary>Analyzes every document; returns an empty array when no compilation is available.</summary>
    public static ImmutableArray<TangentDiagnostic> Analyze(
        ImmutableArray<TanDocument> documents,
        CompilationContext? compilation)
    {
        if (compilation is null || documents.IsDefaultOrEmpty) return ImmutableArray<TangentDiagnostic>.Empty;

        var results = ImmutableArray.CreateBuilder<TangentDiagnostic>();
        foreach (TanDocument document in documents)
        {
            Walk(document, document.Tree.Root, "<file>", compilation.Types, results);
        }

        return results.ToImmutable();
    }

    private static void Walk(
        TanDocument document,
        int nodeIndex,
        string contract,
        TypeIndex types,
        ImmutableArray<TangentDiagnostic>.Builder results)
    {
        if (nodeIndex < 0) return;
        CoreSyntaxTree tree = document.Tree;
        NodeList nodes = tree.Nodes;
        Node node = nodes[nodeIndex];
        string scope = contract;

        if (node.Kind == NodeKind.Contract)
        {
            var name = new StringId(node.Payload);
            if (name.IsValid) scope = tree.Strings.GetString(name);
        }
        else if (node.Kind == NodeKind.TypePattern)
        {
            var name = new StringId(node.Payload);
            if (name.IsValid)
            {
                string text = tree.Strings.GetString(name);
                if (text.Length > 0 && !types.TryResolve(text, out INamedTypeSymbol _))
                {
                    int length = text.Length <= node.Span.Length ? text.Length : node.Span.Length;
                    results.Add(TangentDiagnostic.Create(
                        DiagnosticCatalog.UnresolvedTypeName,
                        new SourceSpan(node.Span.FileIndex, node.Span.Start, length, node.Span.Line, node.Span.Column),
                        document.Path,
                        text,
                        scope));
                }
            }
        }

        foreach (int child in nodes.Children(nodeIndex))
        {
            Walk(document, child, scope, types, results);
        }
    }
}
