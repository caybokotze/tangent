using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Tangent.Analyzers;

namespace Tangent.Graph;

/// <summary>
/// A type the analyzer bound, flattened out of its <see cref="INamedTypeSymbol"/> while the compilation
/// that produced it is still alive.
/// </summary>
public sealed class ExportedType
{
    internal ExportedType(
        string id,
        string name,
        string kind,
        string? layer,
        ImmutableArray<string> contracts,
        string file,
        int line,
        ImmutableArray<GraphMember> members)
    {
        Id = id;
        Name = name;
        Kind = kind;
        Layer = layer;
        Contracts = contracts;
        File = file;
        Line = line;
        Members = members;
    }

    /// <summary>The fully qualified metadata name.</summary>
    public string Id { get; }

    /// <summary>The simple name.</summary>
    public string Name { get; }

    /// <summary><c>class</c>, <c>record</c>, <c>struct</c>, <c>interface</c> or <c>enum</c>.</summary>
    public string Kind { get; }

    /// <summary>The layer the type lives in, or null.</summary>
    public string? Layer { get; }

    /// <summary>The contracts bound to the type; empty when it is bound to none.</summary>
    public ImmutableArray<string> Contracts { get; }

    /// <summary>The absolute path of the declaring file, or "" when the type has no source location.</summary>
    public string File { get; }

    /// <summary>The 1-based declaration line, or 0.</summary>
    public int Line { get; }

    /// <summary>The type's public members.</summary>
    public ImmutableArray<GraphMember> Members { get; }
}

/// <summary>One aggregated (from, to) pair, with how often it was seen and whether it breaks layering.</summary>
public sealed class ExportedReference
{
    internal ExportedReference(string from, string to)
    {
        From = from;
        To = to;
    }

    /// <summary>The referencing type's id.</summary>
    public string From { get; }

    /// <summary>The referenced type's id.</summary>
    public string To { get; }

    /// <summary>How many references were observed.</summary>
    public int Count;

    /// <summary>True when at least one of them was forbidden by the layer matrix.</summary>
    public bool ViolatesLayering;

    /// <summary>True when the referenced type has source in the solution rather than living in metadata.</summary>
    public bool TargetIsInSource;
}

/// <summary>A C# source file of an analysed project, with the types it declares.</summary>
public sealed class ExportedFile
{
    internal ExportedFile(string path, string project, int lines, ImmutableArray<string> types)
    {
        Path = path;
        Project = project;
        Lines = lines;
        Types = types;
    }

    /// <summary>The absolute path.</summary>
    public string Path { get; }

    /// <summary>The project that compiles the file. For a file shared by several projects, the first by name.</summary>
    public string Project { get; }

    /// <summary>The number of lines.</summary>
    public int Lines { get; }

    /// <summary>The ids of the declared types, nested types included, in declaration order.</summary>
    public ImmutableArray<string> Types { get; }
}

/// <summary>
/// The <see cref="IReferenceSink"/> <c>tangent graph</c> registers on the analyzer, so the graph is a
/// by-product of the same Tier 1 walk that produces the diagnostics rather than a second pass.
/// Every member is safe to call from concurrent analyzer callbacks.
/// </summary>
public sealed class GraphExporter : IReferenceSink
{
    private static readonly SymbolDisplayFormat IdFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.None,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.None);

    private static readonly SymbolDisplayFormat SignatureFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeParameters | SymbolDisplayMemberOptions.IncludeType,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                              SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    private readonly ConcurrentDictionary<string, ExportedType> _types = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(string From, string To), ExportedReference> _references = new();

    /// <summary>Every type the analyzer reported, keyed by id. Shared types seen in several projects appear once.</summary>
    public IReadOnlyCollection<ExportedType> Types => _types.Values.ToArray();

    /// <summary>Every (from, to) pair the Tier 1 walk observed.</summary>
    public IReadOnlyCollection<ExportedReference> References => _references.Values.ToArray();

    /// <summary>Every C# source file recorded through <see cref="AddSourceFiles"/>, keyed by path.</summary>
    public IReadOnlyCollection<ExportedFile> Files => _files.Values.ToArray();

    private readonly ConcurrentDictionary<string, ExportedFile> _files = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Records the hand-written source files of one compilation and the types each declares, so the graph
    /// can show files that no contract covers. Generated files (under <c>obj/</c>, <c>*.g.cs</c>, or with no
    /// file on disk) are skipped. Safe to call for several projects concurrently.
    /// </summary>
    public void AddSourceFiles(Compilation compilation, string projectName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        foreach (SyntaxTree tree in compilation.SyntaxTrees)
        {
            string path = tree.FilePath;
            if (!IsHandWritten(path)) continue;

            SyntaxNode root = tree.GetRoot(cancellationToken);
            SemanticModel semanticModel = compilation.GetSemanticModel(tree);

            ImmutableArray<string>.Builder types = ImmutableArray.CreateBuilder<string>();
            foreach (SyntaxNode node in root.DescendantNodes(static n => n is not BaseMethodDeclarationSyntax and not AccessorListSyntax))
            {
                if (node is not (BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)) continue;
                if (semanticModel.GetDeclaredSymbol(node, cancellationToken) is INamedTypeSymbol type)
                {
                    string id = IdOf(type);
                    if (!types.Contains(id)) types.Add(id);
                }
            }

            var file = new ExportedFile(path, projectName, tree.GetText(cancellationToken).Lines.Count, types.ToImmutable());
            _files.AddOrUpdate(
                path,
                file,
                (_, existing) => string.CompareOrdinal(projectName, existing.Project) < 0 ? file : existing);
        }
    }

    private static bool IsHandWritten(string path)
    {
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return false;
        if (path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)) return false;

        string normalized = path.Replace('\\', '/');
        return !normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase) &&
               !normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The fully qualified metadata-style id this exporter gives a type.</summary>
    public static string IdOf(INamedTypeSymbol type)
    {
        INamedTypeSymbol definition = type.OriginalDefinition;
        string name = definition.ToDisplayString(IdFormat);
        return definition.Arity > 0 ? name + "`" + definition.Arity.ToString(System.Globalization.CultureInfo.InvariantCulture) : name;
    }

    /// <inheritdoc/>
    public void OnTypeBound(INamedTypeSymbol type, ImmutableArray<string> contractNames, int? layerIndex, string? layerName)
    {
        if (type is null) return;

        string id = IdOf(type);

        // The same type is analysed once per project that compiles it. Keep the first sighting that
        // carries contracts so a multi-targeted or linked file does not lose its binding.
        if (_types.TryGetValue(id, out ExportedType? existing) && !existing.Contracts.IsEmpty) return;

        Location? location = null;
        foreach (Location candidate in type.Locations)
        {
            if (candidate.IsInSource) { location = candidate; break; }
        }

        FileLinePositionSpan position = location?.GetLineSpan() ?? default;

        var exported = new ExportedType(
            id,
            type.Name,
            KindOf(type),
            layerName,
            contractNames.IsDefault ? ImmutableArray<string>.Empty : contractNames,
            location is null ? string.Empty : position.Path,
            location is null ? 0 : position.StartLinePosition.Line + 1,
            MembersOf(type));

        _types[id] = exported;
    }

    /// <inheritdoc/>
    public void OnReference(INamedTypeSymbol from, INamedTypeSymbol to, ReferenceKind kind, Location location, bool violatesLayering)
    {
        if (from is null || to is null) return;

        string fromId = IdOf(from);
        string toId = IdOf(to);
        if (string.Equals(fromId, toId, StringComparison.Ordinal)) return;

        ExportedReference edge = _references.GetOrAdd((fromId, toId), static key => new ExportedReference(key.From, key.To));

        bool inSource = false;
        foreach (Location candidate in to.OriginalDefinition.Locations)
        {
            if (candidate.IsInSource) { inSource = true; break; }
        }

        lock (edge)
        {
            edge.Count++;
            edge.ViolatesLayering |= violatesLayering;
            edge.TargetIsInSource |= inSource;
        }
    }

    private static string KindOf(INamedTypeSymbol type)
    {
        if (type.IsRecord) return "record";
        return type.TypeKind switch
        {
            TypeKind.Interface => "interface",
            TypeKind.Struct => "struct",
            TypeKind.Enum => "enum",
            TypeKind.Delegate => "delegate",
            _ => "class",
        };
    }

    private static ImmutableArray<GraphMember> MembersOf(INamedTypeSymbol type)
    {
        ImmutableArray<ISymbol> members = type.GetMembers();
        if (members.IsDefaultOrEmpty) return ImmutableArray<GraphMember>.Empty;

        ImmutableArray<GraphMember>.Builder builder = ImmutableArray.CreateBuilder<GraphMember>();
        foreach (ISymbol member in members)
        {
            if (member.IsImplicitlyDeclared) continue;
            if (member.DeclaredAccessibility != Accessibility.Public) continue;

            string? kind = MemberKindOf(member);
            if (kind is null) continue;

            builder.Add(new GraphMember(
                kind == "constructor" ? ".ctor" : member.Name,
                kind,
                member.ToDisplayString(SignatureFormat)));
        }

        builder.Sort(static (a, b) =>
        {
            int byKind = string.CompareOrdinal(a.Kind, b.Kind);
            return byKind != 0 ? byKind : string.CompareOrdinal(a.Signature, b.Signature);
        });

        return builder.ToImmutable();
    }

    private static string? MemberKindOf(ISymbol member)
    {
        switch (member)
        {
            case IMethodSymbol method:
                return method.MethodKind switch
                {
                    MethodKind.Constructor => "constructor",
                    MethodKind.Ordinary => "method",
                    _ => null,
                };
            case IPropertySymbol: return "property";
            case IFieldSymbol field when !field.IsImplicitlyDeclared && field.AssociatedSymbol is null: return "field";
            case IEventSymbol: return "event";
            default: return null;
        }
    }
}
