using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Tangent.Core.Ir;
using Tangent.Core.Matching;
using Tangent.LanguageServer.Roslyn;
using Tangent.LanguageServer.Syntax;
using Tangent.LanguageServer.Workspace;

namespace Tangent.LanguageServer.Features;

/// <summary>Builds the Markdown shown on hover for contracts, layers and type patterns.</summary>
internal static class HoverBuilder
{
    /// <summary>Renders hover content for a region, or null when there is nothing useful to say.</summary>
    public static string? Build(WorkspaceSnapshot snapshot, TanRegion region, CompilationContext? compilation)
    {
        ArchitectureModel? model = snapshot.Model;
        if (region.IsContract && model is not null) return Contract(model, region.Text, compilation);
        if (region.IsLayer && model is not null) return Layer(model, region.Text);
        if (region.IsTypeName) return TypeName(region.Text, compilation);
        if (region.Kind == TanRegionKind.TypeTemplate) return Template(region.Text);
        return null;
    }

    private static string? Contract(ArchitectureModel model, string name, CompilationContext? compilation)
    {
        int index = model.FindContract(name.AsSpan());
        if (index < 0) return "**contract** `" + name + "`\n\nNot declared in this `.tangent` folder.";

        Contract contract = model.Contracts[index];
        var text = new StringBuilder();
        text.Append("**contract** `").Append(name).Append("`\n\n");

        if (contract.ExtendsIndex >= 0)
        {
            text.Append("- extends `").Append(model.Text(model.Contracts[contract.ExtendsIndex].Name)).Append("`\n");
        }

        if (contract.Match is MatchClause match)
        {
            text.Append("- `match: ").Append(IrText.DescribeMatch(model, match)).Append("`\n");
        }

        int own = 0;
        foreach (Rule rule in contract.Rules)
        {
            if (rule.OriginContract == index) own++;
        }

        text.Append("- ").Append(Count(contract.Rules.Length, "rule"));
        if (own != contract.Rules.Length)
        {
            text.Append(" (").Append(own.ToString(CultureInfo.InvariantCulture)).Append(" own, ")
                .Append((contract.Rules.Length - own).ToString(CultureInfo.InvariantCulture)).Append(" inherited)");
        }

        text.Append('\n');

        if (compilation is null)
        {
            text.Append("\n_Bound types are listed once the C# workspace has loaded._");
            return text.ToString();
        }

        BoundTypeSet bound = ContractBinding.Evaluate(model, index, compilation);
        text.Append("\n**Bound types**");
        if (bound.Approximate) text.Append(" (approximate)");
        text.Append("\n\n");

        if (bound.Types.IsEmpty)
        {
            text.Append("_none_\n");
        }
        else
        {
            foreach (string type in bound.Types) text.Append("- `").Append(type).Append("`\n");
            if (bound.Truncated) text.Append("- _…and more_\n");
        }

        if (bound.Approximate)
        {
            text.Append("\n_`inherits`, `implements` and `attributes` conditions in the `match` clause are not evaluated here, so this list may be wider than the real binding._");
        }

        return text.ToString();
    }

    private static string? Layer(ArchitectureModel model, string name)
    {
        int index = model.FindLayer(name.AsSpan());
        if (index < 0) return "**layer** `" + name + "`\n\nNot declared in this `.tangent` folder.";

        Tangent.Core.Ir.Layer layer = model.Layers[index];
        var text = new StringBuilder();
        text.Append("**layer** `").Append(name).Append("` = `").Append(model.Globs[layer.GlobIndex].Text).Append("`\n\n");

        ImmutableArray<int> targets = model.Dependencies.AllowedTargets(index);
        var others = new List<string>();
        foreach (int target in targets)
        {
            if (target == index) continue;
            others.Add(model.Text(model.Layers[target].Name));
        }

        text.Append("May depend on: ");
        text.Append(others.Count == 0 ? "_nothing outside itself_" : "`" + string.Join("`, `", others) + "`");
        text.Append("\n\n");

        if (!model.AllowedFrameworkGlobs.IsDefaultOrEmpty)
        {
            var framework = new List<string>();
            foreach (int globIndex in model.AllowedFrameworkGlobs) framework.Add(model.Globs[globIndex].Text);
            text.Append("Always allowed: `").Append(string.Join("`, `", framework)).Append('`');
        }

        return text.ToString();
    }

    private static string TypeName(string name, CompilationContext? compilation)
    {
        if (compilation is null)
        {
            return "**type pattern** `" + name + "`\n\n_Not resolved: the C# workspace has not loaded._";
        }

        if (!compilation.Types.TryResolve(name, out INamedTypeSymbol symbol))
        {
            return "**type pattern** `" + name + "`\n\n**unresolved** — no type with this name is visible to the workspace.";
        }

        var text = new StringBuilder();
        text.Append("```csharp\n").Append(symbol.ToDisplayString()).Append("\n```\n");

        string? summary = SummaryOf(symbol);
        if (summary is not null) text.Append('\n').Append(summary).Append('\n');

        string ns = TypeIndex.NamespaceOf(symbol);
        if (ns.Length > 0) text.Append("\nNamespace `").Append(ns).Append('`');
        return text.ToString();
    }

    private static string Template(string text) =>
        "**template** `" + text + "`\n\nExpanded per bound type: `{Name}` is the simple type name, " +
        "`{Namespace}` its containing namespace, `{Name:strip Suffix}` the name with a trailing suffix removed.";

    /// <summary>The <c>&lt;summary&gt;</c> element of a symbol's XML documentation, flattened, or null.</summary>
    public static string? SummaryOf(ISymbol symbol)
    {
        string? xml = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrWhiteSpace(xml)) return null;

        const string open = "<summary>";
        const string close = "</summary>";
        int start = xml.IndexOf(open, StringComparison.Ordinal);
        if (start < 0) return null;
        start += open.Length;
        int end = xml.IndexOf(close, start, StringComparison.Ordinal);
        if (end < 0) return null;

        string body = xml.Substring(start, end - start);
        var flattened = new StringBuilder(body.Length);
        bool inTag = false;
        foreach (char c in body)
        {
            if (c == '<') { inTag = true; continue; }
            if (c == '>') { inTag = false; continue; }
            if (inTag) continue;
            flattened.Append(c == '\n' || c == '\r' || c == '\t' ? ' ' : c);
        }

        string result = System.Text.RegularExpressions.Regex.Replace(flattened.ToString(), "  +", " ").Trim();
        return result.Length == 0 ? null : result;
    }

    private static string Count(int value, string noun) =>
        value.ToString(CultureInfo.InvariantCulture) + " " + noun + (value == 1 ? string.Empty : "s");
}
