using System.Collections.Immutable;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Tangent.Core.Ir;
using Tangent.LanguageServer.Roslyn;
using Tangent.LanguageServer.Workspace;

namespace Tangent.LanguageServer.Features;

/// <summary>Produces the completion list for an analysed cursor position.</summary>
internal static class CompletionEngine
{
    /// <summary>The most C#-derived items ever returned in one list.</summary>
    public const int MaxSymbolItems = 200;

    private static readonly string[] TopLevelKeywords = { "project", "contract", "bind", "exempt" };

    private static readonly string[] ProjectKeywords = { "root", "layer", "allow", "nothing" };

    private static readonly string[] ContractKeywords =
    {
        "match", "require", "require-each", "forbid", "where",
        "ctor", "methods", "properties", "fields", "events", "nested",
        "inherits", "implements", "attributes", "returns", "injects", "only",
        "using", "new", "interface", "lives", "in", "stateless",
        "get", "set", "init",
        "public", "private", "protected", "internal",
        "static", "sealed", "abstract", "readonly", "partial",
        "class", "record", "struct", "enum",
        "each", "parameter", "assigned", "field", "null-checked", "not", "stored",
        "name", "starts", "ends", "with", "contains", "matches", "and", "or",
    };

    /// <summary>Builds the completion list.</summary>
    public static CompletionList Build(
        TanCompletionContext context,
        WorkspaceSnapshot snapshot,
        CompilationContext? compilation)
    {
        ArchitectureModel? model = snapshot.Model;
        return context.Kind switch
        {
            CompletionContextKind.TopLevel => Keywords(TopLevelKeywords),
            CompletionContextKind.ProjectBody => Merge(Keywords(ProjectKeywords), Layers(model)),
            CompletionContextKind.ContractBody => Keywords(ContractKeywords),
            CompletionContextKind.Layers => Layers(model),
            CompletionContextKind.Contracts => Contracts(model),
            CompletionContextKind.TypeNames => TypeNames(compilation, context.Prefix),
            CompletionContextKind.Namespaces => Namespaces(compilation, context.Prefix),
            CompletionContextKind.Attributes => Attributes(compilation, context.Prefix),
            _ => new CompletionList(),
        };
    }

    private static CompletionList Merge(CompletionList first, CompletionList second) =>
        new CompletionList(first.Items.Concat(second.Items), isIncomplete: first.IsIncomplete || second.IsIncomplete);

    private static CompletionList Keywords(IEnumerable<string> words) =>
        new CompletionList(words.Select(static word => new CompletionItem
        {
            Label = word,
            Kind = CompletionItemKind.Keyword,
            SortText = "1" + word,
        }));

    private static CompletionList Layers(ArchitectureModel? model)
    {
        if (model is null) return new CompletionList();
        var items = new List<CompletionItem>(model.Layers.Length);
        foreach (Layer layer in model.Layers)
        {
            string name = model.Text(layer.Name);
            items.Add(new CompletionItem
            {
                Label = name,
                Kind = CompletionItemKind.Module,
                Detail = model.Globs[layer.GlobIndex].Text,
                SortText = "0" + name,
            });
        }

        return new CompletionList(items);
    }

    private static CompletionList Contracts(ArchitectureModel? model)
    {
        if (model is null) return new CompletionList();
        var items = new List<CompletionItem>(model.Contracts.Length);
        foreach (Contract contract in model.Contracts)
        {
            string name = model.Text(contract.Name);
            items.Add(new CompletionItem
            {
                Label = name,
                Kind = CompletionItemKind.Class,
                Detail = contract.Match is MatchClause match ? IrText.DescribeMatch(model, match) : "bind only",
                SortText = "0" + name,
            });
        }

        return new CompletionList(items);
    }

    private static CompletionList TypeNames(CompilationContext? compilation, string prefix)
    {
        if (compilation is null) return new CompletionList();
        return FromNames(compilation.Types.SimpleNames, prefix, CompletionItemKind.Class);
    }

    private static CompletionList Namespaces(CompilationContext? compilation, string prefix)
    {
        if (compilation is null) return new CompletionList();
        string bare = prefix.TrimEnd('*', '.');
        return FromNames(compilation.Types.Namespaces, bare, CompletionItemKind.Module);
    }

    private static CompletionList Attributes(CompilationContext? compilation, string prefix)
    {
        if (compilation is null) return new CompletionList();
        return FromNames(compilation.Types.AttributeNames, prefix, CompletionItemKind.Property);
    }

    private static CompletionList FromNames(ImmutableArray<string> names, string prefix, CompletionItemKind kind)
    {
        var items = new List<CompletionItem>(Math.Min(MaxSymbolItems, names.Length));
        bool truncated = false;
        foreach (string name in names)
        {
            if (prefix.Length > 0 && !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (items.Count == MaxSymbolItems)
            {
                truncated = true;
                break;
            }

            items.Add(new CompletionItem
            {
                Label = name,
                Kind = kind,
                SortText = "0" + name,
            });
        }

        return new CompletionList(items, isIncomplete: truncated);
    }
}
