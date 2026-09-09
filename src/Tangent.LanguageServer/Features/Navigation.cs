using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Tangent.LanguageServer.Roslyn;
using Tangent.LanguageServer.Syntax;
using Tangent.LanguageServer.Text;
using Tangent.LanguageServer.Workspace;

namespace Tangent.LanguageServer.Features;

/// <summary>Go-to-definition and find-references over <c>.tan</c> files and, when loaded, C# source.</summary>
internal static class Navigation
{
    /// <summary>The location of <c>contract &lt;name&gt;</c>, or null when no such contract is declared.</summary>
    public static LspLocation? ContractDeclaration(WorkspaceSnapshot snapshot, string name) =>
        FirstRegion(snapshot, TanRegionKind.ContractDeclaration, name);

    /// <summary>The location of <c>layer &lt;name&gt;</c>, or null.</summary>
    public static LspLocation? LayerDeclaration(WorkspaceSnapshot snapshot, string name) =>
        FirstRegion(snapshot, TanRegionKind.LayerDeclaration, name);

    private static LspLocation? FirstRegion(WorkspaceSnapshot snapshot, TanRegionKind kind, string name)
    {
        foreach (TanDocument document in snapshot.Documents)
        {
            foreach (TanRegion region in document.Regions)
            {
                if (region.Kind != kind) continue;
                if (!string.Equals(region.Text, name, StringComparison.Ordinal)) continue;
                return ToLocation(document, region);
            }
        }

        return null;
    }

    /// <summary>The C# declarations of a type pattern name, empty when unresolved or not loaded.</summary>
    public static ImmutableArray<LspLocation> TypeDeclarations(CompilationContext? compilation, string name)
    {
        if (compilation is null) return ImmutableArray<LspLocation>.Empty;
        if (!compilation.Types.TryResolve(name, out INamedTypeSymbol symbol)) return ImmutableArray<LspLocation>.Empty;

        var results = ImmutableArray.CreateBuilder<LspLocation>();
        foreach (Microsoft.CodeAnalysis.Location location in symbol.Locations)
        {
            if (!location.IsInSource) continue;
            Microsoft.CodeAnalysis.SyntaxTree? tree = location.SourceTree;
            if (tree is null || string.IsNullOrEmpty(tree.FilePath)) continue;

            FileLinePositionSpan lines = location.GetLineSpan();
            results.Add(new LspLocation
            {
                Uri = LspText.ToUri(tree.FilePath),
                Range = new LspRange(
                    new LspPosition(lines.StartLinePosition.Line, lines.StartLinePosition.Character),
                    new LspPosition(lines.EndLinePosition.Line, lines.EndLinePosition.Character)),
            });
        }

        return results.ToImmutable();
    }

    /// <summary>
    /// Every mention of a contract: its declaration, every <c>extends</c>, <c>bind</c> and
    /// <c>exempt</c> reference in the <c>.tan</c> files, and every <c>[Tangent("name")]</c>
    /// attribute in C# source once the compilation has loaded.
    /// </summary>
    public static ImmutableArray<LspLocation> ContractReferences(
        WorkspaceSnapshot snapshot,
        string name,
        CompilationContext? compilation,
        bool includeDeclaration)
    {
        var results = ImmutableArray.CreateBuilder<LspLocation>();
        foreach (TanDocument document in snapshot.Documents)
        {
            foreach (TanRegion region in document.Regions)
            {
                if (!region.IsContract) continue;
                if (region.Kind == TanRegionKind.ContractDeclaration && !includeDeclaration) continue;
                if (!string.Equals(region.Text, name, StringComparison.Ordinal)) continue;
                results.Add(ToLocation(document, region));
            }
        }

        if (compilation is not null)
        {
            foreach (AttributeBinding binding in compilation.FindTangentAttributes(name))
            {
                results.Add(new LspLocation
                {
                    Uri = LspText.ToUri(binding.FilePath),
                    Range = new LspRange(
                        new LspPosition(binding.Line, binding.Character),
                        new LspPosition(binding.EndLine, binding.EndCharacter)),
                });
            }
        }

        return results.ToImmutable();
    }

    /// <summary>Every mention of a layer in the <c>.tan</c> files.</summary>
    public static ImmutableArray<LspLocation> LayerReferences(
        WorkspaceSnapshot snapshot,
        string name,
        bool includeDeclaration)
    {
        var results = ImmutableArray.CreateBuilder<LspLocation>();
        foreach (TanDocument document in snapshot.Documents)
        {
            foreach (TanRegion region in document.Regions)
            {
                if (!region.IsLayer) continue;
                if (region.Kind == TanRegionKind.LayerDeclaration && !includeDeclaration) continue;
                if (!string.Equals(region.Text, name, StringComparison.Ordinal)) continue;
                results.Add(ToLocation(document, region));
            }
        }

        return results.ToImmutable();
    }

    /// <summary>The LSP location of a classified region.</summary>
    public static LspLocation ToLocation(TanDocument document, TanRegion region) => new LspLocation
    {
        Uri = LspText.ToUri(document.Path),
        Range = LspText.ToRange(document.Lines, region.Start, region.Length),
    };
}
