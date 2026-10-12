using System;
using System.Collections.Immutable;
using Tangent.Core.Matching;
using Tangent.Core.Text;

namespace Tangent.Core.Ir;

/// <summary>A node in the flat condition tree. Leaves use <see cref="Payload"/>; And/Or use <see cref="Left"/> and <see cref="Right"/>.</summary>
public readonly struct Condition : IEquatable<Condition>
{
    /// <summary>The discriminator.</summary>
    public ConditionKind Kind { get; }

    /// <summary>Index of the left operand for And/Or, else -1.</summary>
    public int Left { get; }

    /// <summary>Index of the right operand for And/Or, else -1.</summary>
    public int Right { get; }

    /// <summary>Leaf payload; see <see cref="ConditionKind"/>.</summary>
    public int Payload { get; }

    /// <summary>Creates a condition node.</summary>
    public Condition(ConditionKind kind, int left, int right, int payload)
    {
        Kind = kind;
        Left = left;
        Right = right;
        Payload = payload;
    }

    /// <inheritdoc/>
    public bool Equals(Condition other) =>
        Kind == other.Kind && Left == other.Left && Right == other.Right && Payload == other.Payload;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Condition other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => ((((int)Kind * 397) ^ Left) * 397 ^ Right) * 397 ^ Payload;
}

/// <summary>A contract's convention-based binding clause: <c>match: kind in target [where condition]</c>.</summary>
public readonly struct MatchClause
{
    /// <summary>The C# type kind selected.</summary>
    public TypeKind Kind { get; }

    /// <summary>Index into <see cref="ArchitectureModel.Layers"/>, or -1 when a raw glob is used.</summary>
    public int LayerIndex { get; }

    /// <summary>Index into <see cref="ArchitectureModel.Globs"/>; always valid (a layer's glob when a layer was named).</summary>
    public int GlobIndex { get; }

    /// <summary>Index into <see cref="ArchitectureModel.Conditions"/>, or -1.</summary>
    public int ConditionIndex { get; }

    /// <summary>Where the clause was written.</summary>
    public SourceSpan Span { get; }

    /// <summary>Creates a match clause.</summary>
    public MatchClause(TypeKind kind, int layerIndex, int globIndex, int conditionIndex, SourceSpan span)
    {
        Kind = kind;
        LayerIndex = layerIndex;
        GlobIndex = globIndex;
        ConditionIndex = conditionIndex;
        Span = span;
    }
}

/// <summary>A named region of the codebase, defined by a namespace glob.</summary>
public readonly record struct Layer(StringId Name, int GlobIndex, SourceSpan DeclarationSpan);

/// <summary>A named set of rules describing the shape of a kind of type.</summary>
public sealed class Contract
{
    /// <summary>Creates a contract.</summary>
    public Contract(
        StringId name,
        int extendsIndex,
        MatchClause? match,
        ImmutableArray<Rule> rules,
        SourceSpan declarationSpan)
    {
        Name = name;
        ExtendsIndex = extendsIndex;
        Match = match;
        Rules = rules;
        DeclarationSpan = declarationSpan;
    }

    /// <summary>The contract name.</summary>
    public StringId Name { get; }

    /// <summary>Index of the contract this one extends, or -1.</summary>
    public int ExtendsIndex { get; }

    /// <summary>The convention-based binding clause, when one was written.</summary>
    public MatchClause? Match { get; }

    /// <summary>All rules, inherited ones first, each carrying its <see cref="Rule.OriginContract"/>.</summary>
    public ImmutableArray<Rule> Rules { get; }

    /// <summary>Where the contract was declared.</summary>
    public SourceSpan DeclarationSpan { get; }
}

/// <summary>An explicit <c>bind</c> statement.</summary>
public sealed class Binding
{
    /// <summary>Creates a binding.</summary>
    public Binding(
        StringId typeName,
        ImmutableArray<int> contractIndices,
        ImmutableArray<StringId> excludedAttributes,
        ImmutableArray<StringId> excludedRules,
        SourceSpan span)
    {
        TypeName = typeName;
        ContractIndices = contractIndices;
        ExcludedAttributes = excludedAttributes;
        ExcludedRules = excludedRules;
        Span = span;
    }

    /// <summary>The fully qualified type name as written.</summary>
    public StringId TypeName { get; }

    /// <summary>The contracts the type is bound to.</summary>
    public ImmutableArray<int> ContractIndices { get; }

    /// <summary>Attribute names listed after <c>except</c>; rules requiring these attributes do not apply.</summary>
    public ImmutableArray<StringId> ExcludedAttributes { get; }

    /// <summary>Rule ids listed after <c>except</c>; those rules do not apply to this type.</summary>
    public ImmutableArray<StringId> ExcludedRules { get; }

    /// <summary>Where the statement was written.</summary>
    public SourceSpan Span { get; }
}

/// <summary>An <c>ignore</c> statement: the violations of a type are ignored, with a reason.</summary>
public sealed class Ignore
{
    /// <summary>Creates an ignore.</summary>
    public Ignore(StringId typeName, ImmutableArray<int> contractIndices, StringId reason, SourceSpan span)
    {
        TypeName = typeName;
        ContractIndices = contractIndices;
        Reason = reason;
        Span = span;
    }

    /// <summary>The fully qualified type name as written.</summary>
    public StringId TypeName { get; }

    /// <summary>The contracts to ignore; empty means every contract.</summary>
    public ImmutableArray<int> ContractIndices { get; }

    /// <summary>True when the type is ignored for every contract.</summary>
    public bool AllContracts => ContractIndices.IsDefaultOrEmpty;

    /// <summary>The required reason.</summary>
    public StringId Reason { get; }

    /// <summary>Where the statement was written.</summary>
    public SourceSpan Span { get; }
}

/// <summary>Which layers may reference which, packed into <see cref="ulong"/> bitsets.</summary>
public sealed class LayerDependencyMatrix
{
    private readonly int _count;
    private readonly int _wordsPerRow;
    private readonly ulong[] _bits;
    private readonly ImmutableArray<ImmutableArray<int>> _allowedTargets;

    internal LayerDependencyMatrix(int count, ulong[] bits, ImmutableArray<int> extraAllowedGlobs)
    {
        _count = count;
        _wordsPerRow = count == 0 ? 0 : (count + 63) / 64;
        _bits = bits;
        ExtraAllowedGlobs = extraAllowedGlobs;

        var rows = ImmutableArray.CreateBuilder<ImmutableArray<int>>(count);
        for (int from = 0; from < count; from++)
        {
            var targets = ImmutableArray.CreateBuilder<int>();
            for (int to = 0; to < count; to++)
            {
                if (IsAllowed(from, to)) targets.Add(to);
            }

            rows.Add(targets.ToImmutable());
        }

        _allowedTargets = rows.ToImmutable();
    }

    /// <summary>The number of layers.</summary>
    public int Count => _count;

    /// <summary>Framework namespace globs allowed for every layer, including the implicit <c>System.**</c>.</summary>
    public ImmutableArray<int> ExtraAllowedGlobs { get; }

    /// <summary>True when a type in layer <paramref name="from"/> may reference layer <paramref name="to"/>.</summary>
    public bool IsAllowed(int from, int to)
    {
        if (from < 0 || to < 0 || from >= _count || to >= _count) return false;
        if (from == to) return true;
        int index = from * _wordsPerRow + (to >> 6);
        return (_bits[index] & (1UL << (to & 63))) != 0;
    }

    /// <summary>Every layer index that <paramref name="from"/> may reference, including itself.</summary>
    public ImmutableArray<int> AllowedTargets(int from) =>
        from < 0 || from >= _count ? ImmutableArray<int>.Empty : _allowedTargets[from];
}

/// <summary>The bound, immutable representation of every layer, contract, binding and rule in one <c>.tangent</c> folder.</summary>
public sealed class ArchitectureModel
{
    /// <summary>Creates a model. Produced by the binder; not intended to be constructed by hand.</summary>
    public ArchitectureModel(
        StringId projectName,
        StringId root,
        ImmutableArray<Layer> layers,
        ImmutableArray<Contract> contracts,
        ImmutableArray<Binding> bindings,
        ImmutableArray<Ignore> ignores,
        LayerDependencyMatrix dependencies,
        StringTable strings,
        TypePatternTable typePatterns,
        ImmutableArray<Glob> globs,
        ImmutableArray<AttributeGroup> attributeGroups,
        ImmutableArray<Template> templates,
        ImmutableArray<Condition> conditions,
        ImmutableArray<int> allowedFrameworkGlobs,
        ImmutableArray<SourceFile> files,
        bool strictExternals = false)
    {
        ProjectName = projectName;
        Root = root;
        Layers = layers;
        Contracts = contracts;
        Bindings = bindings;
        Ignores = ignores;
        Dependencies = dependencies;
        Strings = strings;
        TypePatterns = typePatterns;
        Globs = globs;
        AttributeGroups = attributeGroups;
        Templates = templates;
        Conditions = conditions;
        AllowedFrameworkGlobs = allowedFrameworkGlobs;
        Files = files;
        StrictExternals = strictExternals;
    }

    /// <summary>The <c>project "Name"</c> value, or <see cref="StringId.Invalid"/> when no project block was written.</summary>
    public StringId ProjectName { get; }

    /// <summary>The <c>root:</c> value, or <see cref="StringId.Invalid"/>.</summary>
    public StringId Root { get; }

    /// <summary>Declared layers, in declaration order.</summary>
    public ImmutableArray<Layer> Layers { get; }

    /// <summary>Contracts, topologically ordered so a contract always follows the one it extends.</summary>
    public ImmutableArray<Contract> Contracts { get; }

    /// <summary>Explicit <c>bind</c> statements.</summary>
    public ImmutableArray<Binding> Bindings { get; }

    /// <summary><c>ignore</c> statements.</summary>
    public ImmutableArray<Ignore> Ignores { get; }

    /// <summary>The layer dependency matrix.</summary>
    public LayerDependencyMatrix Dependencies { get; }

    /// <summary>Every identifier, interned once.</summary>
    public StringTable Strings { get; }

    /// <summary>Compiled type patterns and their alternation groups.</summary>
    public TypePatternTable TypePatterns { get; }

    /// <summary>Compiled namespace globs.</summary>
    public ImmutableArray<Glob> Globs { get; }

    /// <summary>Compiled attribute groups.</summary>
    public ImmutableArray<AttributeGroup> AttributeGroups { get; }

    /// <summary>Compiled name templates.</summary>
    public ImmutableArray<Template> Templates { get; }

    /// <summary>The flat condition tree; selectors and match clauses index into this.</summary>
    public ImmutableArray<Condition> Conditions { get; }

    /// <summary>Glob indices allowed for every layer regardless of the matrix. Always contains <c>System.**</c>.</summary>
    public ImmutableArray<int> AllowedFrameworkGlobs { get; }

    /// <summary>
    /// True when the project declared an <c>allow</c> list. A namespace that no layer declares is then a
    /// violation unless the allow list matches it, instead of being ignored.
    /// </summary>
    public bool StrictExternals { get; }

    /// <summary>The files the model was bound from, indexed by <see cref="SourceSpan.FileIndex"/>.</summary>
    public ImmutableArray<SourceFile> Files { get; }

    /// <summary>The index of a contract by name, or -1.</summary>
    public int FindContract(ReadOnlySpan<char> name)
    {
        if (!Strings.TryFind(name, out StringId id)) return -1;
        for (int i = 0; i < Contracts.Length; i++)
        {
            if (Contracts[i].Name == id) return i;
        }

        return -1;
    }

    /// <summary>The index of a layer by name, or -1.</summary>
    public int FindLayer(ReadOnlySpan<char> name)
    {
        if (!Strings.TryFind(name, out StringId id)) return -1;
        for (int i = 0; i < Layers.Length; i++)
        {
            if (Layers[i].Name == id) return i;
        }

        return -1;
    }

    /// <summary>
    /// The index of the layer whose glob matches <paramref name="namespaceName"/>, or -1. When several match,
    /// the one with the most literal segments wins, so a nested layer beats an enclosing one.
    /// </summary>
    public int LayerOf(ReadOnlySpan<char> namespaceName)
    {
        int best = -1;
        int bestScore = -1;
        for (int i = 0; i < Layers.Length; i++)
        {
            Glob glob = Globs[Layers[i].GlobIndex];
            if (!glob.Matches(namespaceName)) continue;
            int score = 0;
            for (int s = 0; s < glob.Segments.Length; s++)
            {
                if (glob.Segments[s].Kind == GlobSegmentKind.Literal) score++;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        return best;
    }

    /// <summary>True when a reference from <paramref name="fromLayer"/> into <paramref name="namespaceName"/> is allowed.</summary>
    public bool IsReferenceAllowed(int fromLayer, ReadOnlySpan<char> namespaceName)
    {
        for (int i = 0; i < AllowedFrameworkGlobs.Length; i++)
        {
            if (Globs[AllowedFrameworkGlobs[i]].Matches(namespaceName)) return true;
        }

        int target = LayerOf(namespaceName);

        // Outside every declared layer: an external package, or code nobody has described. Without an
        // allow list that is not this model's business; with one, every dependency is declared or wrong.
        if (target < 0) return !StrictExternals;
        return Dependencies.IsAllowed(fromLayer, target);
    }

    /// <summary>The text of an interned name.</summary>
    public string Text(StringId id) => Strings.GetString(id);

    /// <summary>The path of the file a span points into, or null.</summary>
    public string? FilePathOf(SourceSpan span) =>
        span.FileIndex >= 0 && span.FileIndex < Files.Length ? Files[span.FileIndex].Path : null;
}
