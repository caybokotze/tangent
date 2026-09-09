using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Tangent.LanguageServer.Roslyn;

/// <summary>
/// A flat, name-oriented view of every named type visible to the workspace's compilations.
/// The language server never needs a semantic model: it only needs to answer "does this
/// pattern name resolve, and to what?", which is a name lookup.
/// </summary>
internal sealed class TypeIndex
{
    private readonly Dictionary<string, ImmutableArray<INamedTypeSymbol>> _bySimpleName;

    private TypeIndex(
        Dictionary<string, ImmutableArray<INamedTypeSymbol>> bySimpleName,
        ImmutableArray<INamedTypeSymbol> allTypes,
        ImmutableArray<INamedTypeSymbol> sourceTypes,
        ImmutableArray<string> simpleNames,
        ImmutableArray<string> namespaces,
        ImmutableArray<string> attributeNames)
    {
        _bySimpleName = bySimpleName;
        AllTypes = allTypes;
        SourceTypes = sourceTypes;
        SimpleNames = simpleNames;
        Namespaces = namespaces;
        AttributeNames = attributeNames;
    }

    /// <summary>Every indexed type, including types from referenced assemblies.</summary>
    public ImmutableArray<INamedTypeSymbol> AllTypes { get; }

    /// <summary>Only the types declared in the workspace's own projects.</summary>
    public ImmutableArray<INamedTypeSymbol> SourceTypes { get; }

    /// <summary>Every distinct simple type name, sorted.</summary>
    public ImmutableArray<string> SimpleNames { get; }

    /// <summary>Every distinct namespace, sorted.</summary>
    public ImmutableArray<string> Namespaces { get; }

    /// <summary>Every attribute type name with the <c>Attribute</c> suffix stripped, sorted.</summary>
    public ImmutableArray<string> AttributeNames { get; }

    /// <summary>Builds an index over the given compilations.</summary>
    public static TypeIndex Build(ImmutableArray<Compilation> compilations)
    {
        var bySimpleName = new Dictionary<string, List<INamedTypeSymbol>>(StringComparer.Ordinal);
        var namespaces = new HashSet<string>(StringComparer.Ordinal);
        var attributes = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var all = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        var sourceTypes = ImmutableArray.CreateBuilder<INamedTypeSymbol>();

        // Source assemblies first. A project reference makes one project's types visible as
        // metadata in another, and the dedupe below keeps whichever was seen first — so if
        // references went first, a project's own types would be indexed as "not from source".
        foreach (Compilation compilation in compilations)
        {
            Collect(compilation.Assembly.GlobalNamespace, sourceOnly: true);
        }

        foreach (Compilation compilation in compilations)
        {
            foreach (MetadataReference reference in compilation.References)
            {
                if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly)
                {
                    Collect(assembly.GlobalNamespace, sourceOnly: false);
                }
            }
        }

        var byName = new Dictionary<string, ImmutableArray<INamedTypeSymbol>>(bySimpleName.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, List<INamedTypeSymbol>> pair in bySimpleName)
        {
            byName[pair.Key] = pair.Value.ToImmutableArray();
        }

        return new TypeIndex(
            byName,
            all.ToImmutable(),
            sourceTypes.ToImmutable(),
            byName.Keys.OrderBy(static n => n, StringComparer.Ordinal).ToImmutableArray(),
            namespaces.OrderBy(static n => n, StringComparer.Ordinal).ToImmutableArray(),
            attributes.OrderBy(static n => n, StringComparer.Ordinal).ToImmutableArray());

        void Collect(INamespaceSymbol root, bool sourceOnly)
        {
            var stack = new Stack<INamespaceSymbol>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                INamespaceSymbol current = stack.Pop();
                if (!current.IsGlobalNamespace) namespaces.Add(current.ToDisplayString());
                foreach (INamespaceSymbol child in current.GetNamespaceMembers()) stack.Push(child);
                foreach (INamedTypeSymbol type in current.GetTypeMembers()) Add(type, sourceOnly);
            }
        }

        void Add(INamedTypeSymbol type, bool sourceOnly)
        {
            if (!sourceOnly && type.DeclaredAccessibility != Accessibility.Public) return;
            string qualified = QualifiedName(type);
            if (!seen.Add(qualified)) return;

            all.Add(type);
            if (sourceOnly) sourceTypes.Add(type);
            if (!bySimpleName.TryGetValue(type.Name, out List<INamedTypeSymbol>? list))
            {
                list = new List<INamedTypeSymbol>(1);
                bySimpleName[type.Name] = list;
            }

            list.Add(type);
            if (IsAttribute(type)) attributes.Add(StripAttributeSuffix(type.Name));

            foreach (INamedTypeSymbol nested in type.GetTypeMembers()) Add(nested, sourceOnly);
        }
    }

    /// <summary>The namespace-qualified name of a type, nested types joined with dots and no generic arity.</summary>
    public static string QualifiedName(INamedTypeSymbol type)
    {
        var parts = new List<string>(4) { type.Name };
        for (INamedTypeSymbol? containing = type.ContainingType; containing is not null; containing = containing.ContainingType)
        {
            parts.Add(containing.Name);
        }

        INamespaceSymbol? ns = type.ContainingNamespace;
        if (ns is not null && !ns.IsGlobalNamespace) parts.Add(ns.ToDisplayString());
        parts.Reverse();
        return string.Join(".", parts);
    }

    /// <summary>The namespace a type lives in, or the empty string for the global namespace.</summary>
    public static string NamespaceOf(INamedTypeSymbol type)
    {
        INamedTypeSymbol outermost = type;
        while (outermost.ContainingType is not null) outermost = outermost.ContainingType;
        INamespaceSymbol? ns = outermost.ContainingNamespace;
        return ns is null || ns.IsGlobalNamespace ? string.Empty : ns.ToDisplayString();
    }

    /// <summary>True when a type derives from <c>System.Attribute</c>.</summary>
    public static bool IsAttribute(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.Name == "Attribute" && current.ContainingNamespace?.ToDisplayString() == "System") return true;
        }

        return false;
    }

    /// <summary>Removes a trailing <c>Attribute</c> suffix when one is present.</summary>
    public static string StripAttributeSuffix(string name) =>
        name.Length > 9 && name.EndsWith("Attribute", StringComparison.Ordinal)
            ? name.Substring(0, name.Length - 9)
            : name;

    /// <summary>
    /// C# keyword spellings of framework types. <c>forbid returns void</c> is a perfectly ordinary
    /// rule, so <c>void</c> must resolve rather than being reported as an unknown type.
    /// </summary>
    private static readonly Dictionary<string, string> CSharpAliases = new(StringComparer.Ordinal)
    {
        ["void"] = "System.Void",
        ["object"] = "System.Object",
        ["dynamic"] = "System.Object",
        ["string"] = "System.String",
        ["bool"] = "System.Boolean",
        ["byte"] = "System.Byte",
        ["sbyte"] = "System.SByte",
        ["char"] = "System.Char",
        ["decimal"] = "System.Decimal",
        ["double"] = "System.Double",
        ["float"] = "System.Single",
        ["int"] = "System.Int32",
        ["uint"] = "System.UInt32",
        ["long"] = "System.Int64",
        ["ulong"] = "System.UInt64",
        ["short"] = "System.Int16",
        ["ushort"] = "System.UInt16",
        ["nint"] = "System.IntPtr",
        ["nuint"] = "System.UIntPtr",
    };

    /// <summary>Every type with the given simple name.</summary>
    public ImmutableArray<INamedTypeSymbol> WithSimpleName(string name) =>
        _bySimpleName.TryGetValue(name, out ImmutableArray<INamedTypeSymbol> types) ? types : ImmutableArray<INamedTypeSymbol>.Empty;

    /// <summary>
    /// Resolves a Tangent type-pattern name. An undotted name matches that simple name in any
    /// namespace; a dotted name must match the namespace-qualified name as a suffix at a segment
    /// boundary, exactly as <c>Tangent.Core</c> documents for the analyzer.
    /// </summary>
    public bool TryResolve(string patternName, out INamedTypeSymbol symbol)
    {
        symbol = null!;
        if (string.IsNullOrEmpty(patternName)) return false;
        if (CSharpAliases.TryGetValue(patternName, out string? aliased)) patternName = aliased;

        int lastDot = patternName.LastIndexOf('.');
        string simple = lastDot < 0 ? patternName : patternName.Substring(lastDot + 1);
        ImmutableArray<INamedTypeSymbol> candidates = WithSimpleName(simple);
        if (candidates.IsEmpty) return false;

        if (lastDot < 0)
        {
            symbol = candidates[0];
            return true;
        }

        foreach (INamedTypeSymbol candidate in candidates)
        {
            string qualified = QualifiedName(candidate);
            if (qualified.Length < patternName.Length) continue;
            if (!qualified.EndsWith(patternName, StringComparison.Ordinal)) continue;
            if (qualified.Length == patternName.Length || qualified[qualified.Length - patternName.Length - 1] == '.')
            {
                symbol = candidate;
                return true;
            }
        }

        return false;
    }
}
