using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Tangent.Core.Ir;

namespace Tangent.Analyzers.Internal;

/// <summary>A rule together with the contract it came from.</summary>
internal readonly struct AppliedRule
{
    internal AppliedRule(int contractIndex, int ruleIndex)
    {
        ContractIndex = contractIndex;
        RuleIndex = ruleIndex;
    }

    /// <summary>Index into <see cref="ArchitectureModel.Contracts"/> of the contract the type is bound to.</summary>
    internal int ContractIndex { get; }

    /// <summary>Index into that contract's flattened rule list.</summary>
    internal int RuleIndex { get; }
}

/// <summary>An exemption that applied to a type, reported as TNG9000.</summary>
internal readonly struct ExemptionNote
{
    internal ExemptionNote(Location location, string reason)
        : this(location, reason, string.Empty, string.Empty)
    {
    }

    internal ExemptionNote(Location location, string reason, string contract, string ruleId)
    {
        Location = location;
        Reason = reason;
        Contract = contract;
        RuleId = ruleId;
    }

    internal Location Location { get; }

    internal string Reason { get; }

    /// <summary>The contract the removed rule came from, or <c>""</c> when the exemption is not rule-scoped.</summary>
    internal string Contract { get; }

    /// <summary>The removed rule's id, or <c>""</c> when the exemption is not rule-scoped.</summary>
    internal string RuleId { get; }
}

/// <summary>A diagnostic discovered while binding a type, reported from the symbol action.</summary>
internal readonly struct BindingDiagnostic
{
    internal BindingDiagnostic(string code, Location location, string message)
    {
        Code = code;
        Location = location;
        Message = message;
    }

    internal string Code { get; }

    internal Location Location { get; }

    internal string Message { get; }
}

/// <summary>
/// Everything the analyzer needs to know about one named type, computed once per compilation: the layer
/// it lives in, the contracts bound to it, and the rules that survive exemptions, split by tier.
/// </summary>
internal sealed class TypeContext
{
    internal TypeContext(
        INamedTypeSymbol type,
        string namespaceName,
        int layerIndex,
        ImmutableArray<int> contracts,
        ImmutableArray<AppliedRule> symbolRules,
        ImmutableArray<AppliedRule> referenceRules,
        ImmutableArray<AppliedRule> typeUsingRules,
        ImmutableArray<AppliedRule> flowRules,
        ImmutableArray<ExemptionNote> exemptions,
        ImmutableArray<BindingDiagnostic> bindingDiagnostics)
    {
        Type = type;
        NamespaceName = namespaceName;
        LayerIndex = layerIndex;
        Contracts = contracts;
        SymbolRules = symbolRules;
        ReferenceRules = referenceRules;
        TypeUsingRules = typeUsingRules;
        FlowRules = flowRules;
        Exemptions = exemptions;
        BindingDiagnostics = bindingDiagnostics;
    }

    internal INamedTypeSymbol Type { get; }

    /// <summary>The containing namespace, already materialised once per namespace symbol.</summary>
    internal string NamespaceName { get; }

    /// <summary>The layer the namespace falls in, or -1.</summary>
    internal int LayerIndex { get; }

    /// <summary>The contracts bound to this type, after exemptions removed whole contracts.</summary>
    internal ImmutableArray<int> Contracts { get; }

    /// <summary>Tier 0 rules that apply.</summary>
    internal ImmutableArray<AppliedRule> SymbolRules { get; }

    /// <summary>Tier 1 rules that apply.</summary>
    internal ImmutableArray<AppliedRule> ReferenceRules { get; }

    /// <summary>
    /// The type-level <c>forbid using</c> rules, pre-filtered for the symbol-level reference walk over base
    /// types, interfaces, attributes and member signatures.
    /// </summary>
    internal ImmutableArray<AppliedRule> TypeUsingRules { get; }

    /// <summary>Tier 2 rules that apply.</summary>
    internal ImmutableArray<AppliedRule> FlowRules { get; }

    /// <summary>Exemptions that removed rules from this type.</summary>
    internal ImmutableArray<ExemptionNote> Exemptions { get; }

    /// <summary>TNG0001/TNG0002 problems found while binding, reported from the symbol action.</summary>
    internal ImmutableArray<BindingDiagnostic> BindingDiagnostics { get; }

    /// <summary>True when the type is bound to at least one contract.</summary>
    internal bool IsBound => !Contracts.IsDefaultOrEmpty;
}
