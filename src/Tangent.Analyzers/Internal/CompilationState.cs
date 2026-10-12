using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Tangent.Core.Ir;
using Tangent.Core.Text;

namespace Tangent.Analyzers.Internal;

/// <summary>The cached facts about one namespace symbol: its dotted name and the layer it falls in.</summary>
internal sealed class NamespaceInfo
{
    internal NamespaceInfo(string name, int layerIndex)
    {
        Name = name;
        LayerIndex = layerIndex;
    }

    internal string Name { get; }

    internal int LayerIndex { get; }
}

/// <summary>
/// Per-compilation analyzer state: the bound model, the symbol caches required by §12 and the lazily
/// computed type contexts. Every cache is concurrent; nothing here takes a lock.
/// </summary>
internal sealed class CompilationState
{
    private readonly ConcurrentDictionary<ISymbol, NamespaceInfo> _namespaces =
        new ConcurrentDictionary<ISymbol, NamespaceInfo>(SymbolEqualityComparer.Default);

    private readonly ConcurrentDictionary<ISymbol, TypeContext> _types =
        new ConcurrentDictionary<ISymbol, TypeContext>(SymbolEqualityComparer.Default);

    private readonly ConcurrentDictionary<string, Regex> _regexes =
        new ConcurrentDictionary<string, Regex>(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, bool> _typeNameExists =
        new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, bool> _reported =
        new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<ISymbol, bool> _ignoreNotes =
        new ConcurrentDictionary<ISymbol, bool>(SymbolEqualityComparer.Default);

    private readonly ConcurrentDictionary<int, bool> _usedContracts = new ConcurrentDictionary<int, bool>();

    internal CompilationState(
        Compilation compilation,
        ArchitectureModel model,
        IReferenceSink? sink,
        bool includeUnbound)
    {
        Compilation = compilation;
        Model = model;
        Sink = sink;
        IncludeUnbound = includeUnbound;
        HasLayers = model.Layers.Length > 0;

        bool reference = false;
        bool flow = false;
        for (int c = 0; c < model.Contracts.Length && !(reference && flow); c++)
        {
            ImmutableArray<Rule> rules = model.Contracts[c].Rules;
            for (int r = 0; r < rules.Length; r++)
            {
                if (rules[r].Tier == RuleTier.Reference) reference = true;
                else if (rules[r].Tier == RuleTier.Flow) flow = true;
            }
        }

        HasReferenceRules = reference;
        HasFlowRules = flow;
    }

    internal Compilation Compilation { get; }

    internal ArchitectureModel Model { get; }

    internal IReferenceSink? Sink { get; }

    internal bool IncludeUnbound { get; }

    internal bool HasLayers { get; }

    internal bool HasReferenceRules { get; }

    internal bool HasFlowRules { get; }

    /// <summary>True when anything at all needs an operation walk.</summary>
    internal bool NeedsOperationWalk => HasLayers || HasReferenceRules || HasFlowRules || Sink is not null;

    // ------------------------------------------------------------------
    // namespaces and layers
    // ------------------------------------------------------------------

    internal NamespaceInfo GetNamespaceInfo(INamespaceSymbol? ns)
    {
        if (ns is null || ns.IsGlobalNamespace) return GlobalNamespaceInfo;
        if (_namespaces.TryGetValue(ns, out NamespaceInfo? cached)) return cached;

        string name = ns.ToDisplayString();
        var info = new NamespaceInfo(name, Model.LayerOf(name.AsSpan()));
        return _namespaces.GetOrAdd(ns, info);
    }

    private NamespaceInfo? _globalNamespaceInfo;

    private NamespaceInfo GlobalNamespaceInfo =>
        _globalNamespaceInfo ??= new NamespaceInfo(string.Empty, -1);

    internal string LayerName(int index) =>
        index >= 0 && index < Model.Layers.Length ? Model.Text(Model.Layers[index].Name) : string.Empty;

    /// <summary>
    /// True the first time a (from type, member, to type, code, rule) tuple is seen, so one reference pair
    /// is reported at most once per member however many operations mention it.
    /// </summary>
    internal bool ShouldReport(INamedTypeSymbol from, string member, INamedTypeSymbol to, string code, string ruleId)
    {
        string key = string.Concat(
            GetNamespaceInfo(from.ContainingNamespace).Name, ".", from.MetadataName, "|",
            member, "|",
            GetNamespaceInfo(to.ContainingNamespace).Name, ".", to.MetadataName, "|",
            code, "|", ruleId);
        return _reported.TryAdd(key, true);
    }

    /// <summary>
    /// True when at least one type in this compilation bound to the contract. Type names in a contract are
    /// only validated (TNG1020) for contracts that this compilation actually uses, because one
    /// <c>.tangent</c> folder is shared by projects that see very different assemblies.
    /// </summary>
    internal bool IsContractUsed(int contractIndex) => _usedContracts.ContainsKey(contractIndex);

    /// <summary>True the first time an ignore is seen for a member, so TNG9000 is noted once per member.</summary>
    internal bool ShouldNoteIgnore(ISymbol symbol) => _ignoreNotes.TryAdd(symbol, true);

    internal Regex GetRegex(string pattern) =>
        _regexes.GetOrAdd(pattern, static p => new Regex(p, RegexOptions.CultureInvariant));

    // ------------------------------------------------------------------
    // type contexts
    // ------------------------------------------------------------------

    /// <summary>
    /// Returns the context for a type, or null when the type is bound to nothing and lives outside every
    /// layer. The negative path allocates nothing: the probe runs over stack buffers only.
    /// </summary>
    internal TypeContext? GetContext(INamedTypeSymbol type)
    {
        if (_types.TryGetValue(type, out TypeContext? cached)) return cached;

        NamespaceInfo ns = GetNamespaceInfo(type.ContainingNamespace);

        Span<int> buffer = stackalloc int[32];
        int count = ProbeContracts(type, ns, buffer);
        if (count == 0 && ns.LayerIndex < 0 && Sink is null && !HasTangentAttribute(type)) return null;

        TypeContext context = BuildContext(type, ns, buffer, count);
        return _types.GetOrAdd(type, context);
    }

    /// <summary>
    /// Finds the contracts bound to a type, honouring the precedence attribute &gt; bind &gt; match.
    /// Fills <paramref name="buffer"/> with contract indices and returns how many were written.
    /// Allocation-free; unknown contract names are re-discovered by <see cref="BuildContext"/>.
    /// </summary>
    private int ProbeContracts(INamedTypeSymbol type, NamespaceInfo ns, Span<int> buffer)
    {
        int count = 0;

        ImmutableArray<AttributeData> attributes = type.GetAttributes();
        for (int i = 0; i < attributes.Length; i++)
        {
            if (!SymbolFacts.IsTangentAttribute(attributes[i].AttributeClass)) continue;
            ImmutableArray<TypedConstant> arguments = attributes[i].ConstructorArguments;
            if (arguments.Length == 0) continue;
            if (arguments[0].Value is not string name) continue;
            int index = Model.FindContract(name.AsSpan());
            if (index >= 0) count = Add(buffer, count, index);
        }

        if (count > 0) return count;

        ImmutableArray<Binding> bindings = Model.Bindings;
        for (int i = 0; i < bindings.Length; i++)
        {
            if (!FullyQualifiedNameEquals(type, Model.Strings.GetSpan(bindings[i].TypeName))) continue;
            ImmutableArray<int> contracts = bindings[i].ContractIndices;
            for (int c = 0; c < contracts.Length; c++) count = Add(buffer, count, contracts[c]);
        }

        if (count > 0) return count;

        ImmutableArray<Contract> all = Model.Contracts;
        for (int i = 0; i < all.Length; i++)
        {
            MatchClause? match = all[i].Match;
            if (match is null) continue;
            if (!MatchesClause(type, ns, match.Value)) continue;
            count = Add(buffer, count, i);
        }

        return count;
    }

    /// <summary>True when the type carries a Tangent attribute, which must be diagnosed even when unbound.</summary>
    private static bool HasTangentAttribute(INamedTypeSymbol type)
    {
        ImmutableArray<AttributeData> attributes = type.GetAttributes();
        for (int i = 0; i < attributes.Length; i++)
        {
            INamedTypeSymbol? attributeClass = attributes[i].AttributeClass;
            if (SymbolFacts.IsTangentAttribute(attributeClass) || SymbolFacts.IsTangentIgnoreAttribute(attributeClass)) return true;
        }

        return false;
    }

    private static int Add(Span<int> buffer, int count, int value)
    {
        for (int i = 0; i < count; i++)
        {
            if (buffer[i] == value) return count;
        }

        if (count >= buffer.Length) return count;
        buffer[count] = value;
        return count + 1;
    }

    private bool MatchesClause(INamedTypeSymbol type, NamespaceInfo ns, in MatchClause match)
    {
        if (!SymbolFacts.MatchesTypeKind(type, match.Kind)) return false;
        if (match.GlobIndex < 0 || match.GlobIndex >= Model.Globs.Length) return false;
        if (!Model.Globs[match.GlobIndex].Matches(ns.Name.AsSpan())) return false;
        return EvaluateCondition(match.ConditionIndex, type, type);
    }

    /// <summary>Evaluates a flat condition tree against a symbol.</summary>
    internal bool EvaluateCondition(int index, ISymbol symbol, INamedTypeSymbol boundType)
    {
        if (index < 0) return true;
        Condition condition = Model.Conditions[index];
        switch (condition.Kind)
        {
            case ConditionKind.And:
                return EvaluateCondition(condition.Left, symbol, boundType) &&
                       EvaluateCondition(condition.Right, symbol, boundType);
            case ConditionKind.Or:
                return EvaluateCondition(condition.Left, symbol, boundType) ||
                       EvaluateCondition(condition.Right, symbol, boundType);
            case ConditionKind.NameStartsWith:
                return symbol.Name.AsSpan().StartsWith(Model.Strings.GetSpan(new StringId(condition.Payload)), StringComparison.Ordinal);
            case ConditionKind.NameEndsWith:
                return symbol.Name.AsSpan().EndsWith(Model.Strings.GetSpan(new StringId(condition.Payload)), StringComparison.Ordinal);
            case ConditionKind.NameContains:
                return symbol.Name.AsSpan().IndexOf(Model.Strings.GetSpan(new StringId(condition.Payload)), StringComparison.Ordinal) >= 0;
            case ConditionKind.NameMatches:
                return GetRegex(Model.Strings.GetString(new StringId(condition.Payload))).IsMatch(symbol.Name);
            case ConditionKind.Inherits:
                return symbol is INamedTypeSymbol inheriting &&
                       PatternMatching.AnyBaseMatches(Model, condition.Payload, inheriting, NameSpan(boundType), NamespaceSpan(boundType));
            case ConditionKind.Implements:
                return symbol is INamedTypeSymbol implementing &&
                       PatternMatching.AnyInterfaceMatches(Model, condition.Payload, implementing, NameSpan(boundType), NamespaceSpan(boundType));
            case ConditionKind.HasAttributes:
                return PatternMatching.HasAttribute(Model, condition.Payload, symbol);
            case ConditionKind.Not:
                return !EvaluateCondition(condition.Left, symbol, boundType);
            case ConditionKind.Modifier:
                return SymbolFacts.HasModifiers(symbol, (Modifiers)condition.Payload);
            default:
                return true;
        }
    }

    internal ReadOnlySpan<char> NameSpan(INamedTypeSymbol type) => type.Name.AsSpan();

    internal ReadOnlySpan<char> NamespaceSpan(INamedTypeSymbol type) =>
        GetNamespaceInfo(type.ContainingNamespace).Name.AsSpan();

    // ------------------------------------------------------------------
    // building a context
    // ------------------------------------------------------------------

    private TypeContext BuildContext(INamedTypeSymbol type, NamespaceInfo ns, ReadOnlySpan<int> probed, int count)
    {
        var contracts = ImmutableArray.CreateBuilder<int>(count);
        var diagnostics = ImmutableArray.CreateBuilder<BindingDiagnostic>();
        var ignores = ImmutableArray.CreateBuilder<IgnoreNote>();
        for (int i = 0; i < count; i++) contracts.Add(probed[i]);

        // Unknown contract names in [Tangent(...)] are errors, reported at the attribute.
        ImmutableArray<AttributeData> attributes = type.GetAttributes();
        var excludedRuleIds = new List<Exclusion>();
        var excludedAttributeNames = new List<Exclusion>();
        bool ignoreEverything = false;
        var ignoredContracts = new List<int>();

        for (int i = 0; i < attributes.Length; i++)
        {
            AttributeData attribute = attributes[i];
            if (SymbolFacts.IsTangentAttribute(attribute.AttributeClass))
            {
                ImmutableArray<TypedConstant> arguments = attribute.ConstructorArguments;
                if (arguments.Length == 0 || arguments[0].Value is not string name) continue;
                if (Model.FindContract(name.AsSpan()) >= 0) continue;
                diagnostics.Add(new BindingDiagnostic(
                    "TNG0001",
                    SymbolFacts.LocationOf(type, attribute),
                    MessageFactory.UnknownContract(Model, name)));
            }
            else if (SymbolFacts.IsTangentIgnoreAttribute(attribute.AttributeClass))
            {
                ReadIgnore(attribute, type, contracts, diagnostics, ignores,
                    excludedRuleIds, ignoredContracts, ref ignoreEverything);
            }
        }

        // `ignore` statements.
        ImmutableArray<Ignore> statements = Model.Ignores;
        for (int i = 0; i < statements.Length; i++)
        {
            Ignore ignore = statements[i];
            if (!FullyQualifiedNameEquals(type, Model.Strings.GetSpan(ignore.TypeName))) continue;
            ignores.Add(new IgnoreNote(SymbolFacts.LocationOf(type), Model.Text(ignore.Reason)));
            if (ignore.AllContracts)
            {
                ignoreEverything = true;
            }
            else
            {
                for (int c = 0; c < ignore.ContractIndices.Length; c++) ignoredContracts.Add(ignore.ContractIndices[c]);
            }
        }

        // `bind ... except` exclusions. Each one is an ignore, so it carries the reason TNG9000 reports
        // once the rule it removes is known.
        ImmutableArray<Binding> bindings = Model.Bindings;
        for (int i = 0; i < bindings.Length; i++)
        {
            if (!FullyQualifiedNameEquals(type, Model.Strings.GetSpan(bindings[i].TypeName))) continue;
            string origin = MessageFactory.Origin(Model, bindings[i].Span);
            ImmutableArray<StringId> excludedAttributes = bindings[i].ExcludedAttributes;
            for (int a = 0; a < excludedAttributes.Length; a++)
            {
                string name = Model.Text(excludedAttributes[a]);
                excludedAttributeNames.Add(new Exclusion(name, "bind except [" + name + "] (" + origin + ")"));
            }

            ImmutableArray<StringId> excludedRules = bindings[i].ExcludedRules;
            for (int r = 0; r < excludedRules.Length; r++)
            {
                string ruleId = Model.Text(excludedRules[r]);
                excludedRuleIds.Add(new Exclusion(ruleId, "bind except " + ruleId + " (" + origin + ")"));
            }
        }

        if (ignoreEverything) contracts.Clear();
        else if (ignoredContracts.Count > 0)
        {
            for (int i = contracts.Count - 1; i >= 0; i--)
            {
                if (ignoredContracts.Contains(contracts[i])) contracts.RemoveAt(i);
            }
        }

        var symbolRules = ImmutableArray.CreateBuilder<AppliedRule>();
        var referenceRules = ImmutableArray.CreateBuilder<AppliedRule>();
        var typeUsingRules = ImmutableArray.CreateBuilder<AppliedRule>();
        var flowRules = ImmutableArray.CreateBuilder<AppliedRule>();

        var seenRuleIds = new HashSet<string>(StringComparer.Ordinal);
        bool hasExclusions = excludedRuleIds.Count > 0 || excludedAttributeNames.Count > 0;
        for (int i = 0; i < contracts.Count; i++)
        {
            int contractIndex = contracts[i];
            _usedContracts[contractIndex] = true;
            ImmutableArray<Rule> rules = Model.Contracts[contractIndex].Rules;
            for (int r = 0; r < rules.Length; r++)
            {
                Rule rule = rules[r];

                // A type bound to both a contract and one that extends it sees the inherited rules twice.
                if (!seenRuleIds.Add(rule.Id.Value)) continue;

                if (hasExclusions && IsExcluded(rule, excludedRuleIds, excludedAttributeNames, out string? exclusionReason))
                {
                    // Every ignore is visible: an `except` that removes a rule is noted once per
                    // (type, removed rule). `[TangentIgnore(Rule = ...)]` already noted itself.
                    if (exclusionReason is not null)
                    {
                        ignores.Add(new IgnoreNote(
                            SymbolFacts.LocationOf(type),
                            exclusionReason,
                            Model.Text(Model.Contracts[contractIndex].Name),
                            rule.Id.Value));
                    }

                    continue;
                }

                var applied = new AppliedRule(contractIndex, r);
                switch (rule.Tier)
                {
                    case RuleTier.Reference:
                        referenceRules.Add(applied);
                        if (rule.Predicate.Kind is PredicateKind.UsingNamespace or PredicateKind.UsingContract &&
                            rule.Selector.Kind == MemberKind.Type &&
                            EvaluateCondition(rule.Selector.ConditionIndex, type, type))
                        {
                            typeUsingRules.Add(applied);
                        }

                        break;
                    case RuleTier.Flow: flowRules.Add(applied); break;
                    default: symbolRules.Add(applied); break;
                }
            }
        }

        return new TypeContext(
            type,
            ns.Name,
            ns.LayerIndex,
            contracts.ToImmutable(),
            symbolRules.ToImmutable(),
            referenceRules.ToImmutable(),
            typeUsingRules.ToImmutable(),
            flowRules.ToImmutable(),
            ignores.ToImmutable(),
            diagnostics.ToImmutable());
    }

    private void ReadIgnore(
        AttributeData attribute,
        INamedTypeSymbol type,
        ImmutableArray<int>.Builder contracts,
        ImmutableArray<BindingDiagnostic>.Builder diagnostics,
        ImmutableArray<IgnoreNote>.Builder ignores,
        List<Exclusion> excludedRuleIds,
        List<int> ignoredContracts,
        ref bool ignoreEverything)
    {
        string reason = string.Empty;
        ImmutableArray<TypedConstant> arguments = attribute.ConstructorArguments;
        if (arguments.Length > 0 && arguments[0].Value is string text) reason = text;

        string? contractName = null;
        string? ruleId = null;
        ImmutableArray<KeyValuePair<string, TypedConstant>> named = attribute.NamedArguments;
        for (int i = 0; i < named.Length; i++)
        {
            if (string.Equals(named[i].Key, "Contract", StringComparison.Ordinal)) contractName = named[i].Value.Value as string;
            else if (string.Equals(named[i].Key, "Rule", StringComparison.Ordinal)) ruleId = named[i].Value.Value as string;
        }

        Location location = SymbolFacts.LocationOf(type, attribute);

        if (ruleId is not null)
        {
            if (!RuleExists(contracts, ruleId))
            {
                diagnostics.Add(new BindingDiagnostic("TNG0002", location, MessageFactory.UnknownRule(ruleId)));
                return;
            }

            excludedRuleIds.Add(new Exclusion(ruleId, null));
            ignores.Add(new IgnoreNote(location, reason));
            return;
        }

        if (contractName is not null)
        {
            int index = Model.FindContract(contractName.AsSpan());
            if (index < 0)
            {
                diagnostics.Add(new BindingDiagnostic("TNG0001", location, MessageFactory.UnknownContract(Model, contractName)));
                return;
            }

            ignoredContracts.Add(index);
            ignores.Add(new IgnoreNote(location, reason));
            return;
        }

        ignoreEverything = true;
        ignores.Add(new IgnoreNote(location, reason));
    }

    private bool RuleExists(ImmutableArray<int>.Builder contracts, string ruleId)
    {
        for (int i = 0; i < contracts.Count; i++)
        {
            ImmutableArray<Rule> rules = Model.Contracts[contracts[i]].Rules;
            for (int r = 0; r < rules.Length; r++)
            {
                if (string.Equals(rules[r].Id.Value, ruleId, StringComparison.Ordinal)) return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when an <c>except</c> clause removes this rule from the type. <paramref name="reason"/> is the
    /// TNG9000 reason to note, or null when the exclusion already reported its own note.
    /// </summary>
    private bool IsExcluded(in Rule rule, List<Exclusion> excludedRuleIds, List<Exclusion> excludedAttributeNames, out string? reason)
    {
        for (int i = 0; i < excludedRuleIds.Count; i++)
        {
            if (string.Equals(rule.Id.Value, excludedRuleIds[i].Text, StringComparison.Ordinal))
            {
                reason = excludedRuleIds[i].Reason;
                return true;
            }
        }

        if (rule.Predicate.Kind == PredicateKind.HasAttributes)
        {
            for (int i = 0; i < excludedAttributeNames.Count; i++)
            {
                if (PatternMatching.GroupNames(Model, rule.Predicate.Payload, excludedAttributeNames[i].Text.AsSpan()))
                {
                    reason = excludedAttributeNames[i].Reason;
                    return true;
                }
            }
        }

        reason = null;
        return false;
    }

    /// <summary>One <c>except</c> entry: the name or rule id written, and the reason TNG9000 reports for it.</summary>
    private readonly struct Exclusion
    {
        internal Exclusion(string text, string? reason)
        {
            Text = text;
            Reason = reason;
        }

        /// <summary>The attribute name or rule id as written after <c>except</c>.</summary>
        internal string Text { get; }

        /// <summary>The TNG9000 reason, or null when the exclusion reported its own ignore note already.</summary>
        internal string? Reason { get; }
    }

    // ------------------------------------------------------------------
    // names
    // ------------------------------------------------------------------

    /// <summary>True when the type's fully qualified name is exactly <paramml name="expected"/>.</summary>
    internal static bool FullyQualifiedNameEquals(INamedTypeSymbol type, ReadOnlySpan<char> expected)
    {
        int dot = expected.LastIndexOf('.');
        ReadOnlySpan<char> segment = dot < 0 ? expected : expected.Slice(dot + 1);
        if (!segment.SequenceEqual(type.Name.AsSpan())) return false;
        expected = dot < 0 ? ReadOnlySpan<char>.Empty : expected.Slice(0, dot);

        INamedTypeSymbol? containing = type.ContainingType;
        while (containing is not null)
        {
            if (expected.Length == 0) return false;
            dot = expected.LastIndexOf('.');
            segment = dot < 0 ? expected : expected.Slice(dot + 1);
            if (!segment.SequenceEqual(containing.Name.AsSpan())) return false;
            expected = dot < 0 ? ReadOnlySpan<char>.Empty : expected.Slice(0, dot);
            containing = containing.ContainingType;
        }

        INamespaceSymbol? ns = type.ContainingNamespace;
        while (ns is not null && !ns.IsGlobalNamespace)
        {
            if (expected.Length == 0) return false;
            dot = expected.LastIndexOf('.');
            segment = dot < 0 ? expected : expected.Slice(dot + 1);
            if (!segment.SequenceEqual(ns.Name.AsSpan())) return false;
            expected = dot < 0 ? ReadOnlySpan<char>.Empty : expected.Slice(0, dot);
            ns = ns.ContainingNamespace;
        }

        return expected.Length == 0;
    }

    // ------------------------------------------------------------------
    // type resolution (TNG1002 / TNG1020 / interface lives in)
    // ------------------------------------------------------------------

    /// <summary>True when a type with the given name exists anywhere the compilation can see.</summary>
    internal bool TypeNameExists(string name, int arity)
    {
        string key = arity == 0 ? name : name + "`" + arity.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (_typeNameExists.TryGetValue(key, out bool known)) return known;
        bool exists = ComputeTypeNameExists(name, arity);
        _typeNameExists[key] = exists;
        return exists;
    }

    private bool ComputeTypeNameExists(string name, int arity)
    {
        string mangled = arity == 0
            ? name
            : name + "`" + arity.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (name.IndexOf('.') >= 0)
        {
            if (Compilation.GetTypeByMetadataName(mangled) is not null) return true;
            if (arity != 0 && Compilation.GetTypeByMetadataName(name) is not null) return true;

            int dot = name.LastIndexOf('.');
            string simple = name.Substring(dot + 1);
            return SimpleNameExists(simple, arity);
        }

        return SimpleNameExists(name, arity);
    }

    private bool SimpleNameExists(string name, int arity)
    {
        string mangled = arity == 0
            ? name
            : name + "`" + arity.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (AssemblyHasTypeName(Compilation.Assembly, name, mangled)) return true;

        foreach (MetadataReference reference in Compilation.References)
        {
            if (Compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly &&
                AssemblyHasTypeName(assembly, name, mangled))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AssemblyHasTypeName(IAssemblySymbol assembly, string name, string mangled)
    {
        ICollection<string> names = assembly.TypeNames;
        return names.Contains(name) || (!ReferenceEquals(name, mangled) && names.Contains(mangled));
    }

    /// <summary>
    /// True when the namespace named by <paramref name="typeName"/> (everything before the last dot) exists
    /// in this compilation. A <c>bind</c> naming a type from a project this compilation cannot see is not an
    /// error here; the CLI reports it once across the whole solution instead.
    /// </summary>
    internal bool ContainingNamespaceExists(string typeName)
    {
        int lastDot = typeName.LastIndexOf('.');
        if (lastDot <= 0) return true;

        INamespaceSymbol current = Compilation.GlobalNamespace;
        int start = 0;
        while (start < lastDot)
        {
            int next = typeName.IndexOf('.', start);
            if (next < 0 || next > lastDot) next = lastDot;
            string segment = typeName.Substring(start, next - start);

            INamespaceSymbol? found = null;
            foreach (INamespaceSymbol child in current.GetNamespaceMembers())
            {
                if (string.Equals(child.Name, segment, StringComparison.Ordinal))
                {
                    found = child;
                    break;
                }
            }

            if (found is null) return false;
            current = found;
            start = next + 1;
        }

        return true;
    }

    /// <summary>
    /// Finds the interface a <c>interface I{Name} lives in</c> rule refers to: first among the type's own
    /// interfaces, then among the source declarations of the compilation.
    /// </summary>
    internal INamedTypeSymbol? FindInterface(INamedTypeSymbol type, string name)
    {
        ImmutableArray<INamedTypeSymbol> interfaces = type.AllInterfaces;
        for (int i = 0; i < interfaces.Length; i++)
        {
            if (string.Equals(interfaces[i].Name, name, StringComparison.Ordinal)) return interfaces[i];
        }

        foreach (ISymbol symbol in Compilation.GetSymbolsWithName(name, SymbolFilter.Type))
        {
            if (symbol is INamedTypeSymbol candidate && candidate.TypeKind == Microsoft.CodeAnalysis.TypeKind.Interface)
            {
                return candidate;
            }
        }

        return null;
    }
}
