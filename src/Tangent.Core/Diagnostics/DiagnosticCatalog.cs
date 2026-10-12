using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Tangent.Core.Ir;

namespace Tangent.Core.Diagnostics;

/// <summary>The single catalogue of every Tangent diagnostic, shared by Core, the analyzer, the CLI and the language server.</summary>
public static class DiagnosticCatalog
{
    // ---- TNG00xx: binding ------------------------------------------------

    /// <summary>TNG0001: a <c>[Tangent("X")]</c> attribute names a contract that does not exist.</summary>
    public static readonly DiagnosticDescriptor UnknownContractAttribute = new DiagnosticDescriptor(
        "TNG0001", "Contract is not defined",
        "Contract '{0}' is not defined",
        DiagnosticSeverity.Error, DiagnosticCategory.Binding);

    /// <summary>TNG0002: an ignore names a rule id that does not exist.</summary>
    public static readonly DiagnosticDescriptor UnknownRuleInIgnore = new DiagnosticDescriptor(
        "TNG0002", "Rule referenced in an ignore does not exist",
        "Rule '{0}' referenced in an ignore does not exist",
        DiagnosticSeverity.Error, DiagnosticCategory.Binding);

    // ---- TNG01xx: structural (tier 0) ------------------------------------

    /// <summary>TNG0101: a required modifier is missing, or a forbidden one is present.</summary>
    public static readonly DiagnosticDescriptor ModifierViolation = new DiagnosticDescriptor(
        "TNG0101", "Type modifier violation",
        "{0}: type must be {1}",
        DiagnosticSeverity.Warning, DiagnosticCategory.Structural, RuleTier.Symbol);

    /// <summary>TNG0102: the type does not inherit the required base type.</summary>
    public static readonly DiagnosticDescriptor InheritsViolation = new DiagnosticDescriptor(
        "TNG0102", "Base type violation",
        "{0}: type must inherit {1}",
        DiagnosticSeverity.Warning, DiagnosticCategory.Structural, RuleTier.Symbol);

    /// <summary>TNG0103: the type does not implement the required interface.</summary>
    public static readonly DiagnosticDescriptor ImplementsViolation = new DiagnosticDescriptor(
        "TNG0103", "Interface violation",
        "{0}: type must implement {1}",
        DiagnosticSeverity.Warning, DiagnosticCategory.Structural, RuleTier.Symbol);

    /// <summary>TNG0104: a required attribute is missing.</summary>
    public static readonly DiagnosticDescriptor AttributeViolation = new DiagnosticDescriptor(
        "TNG0104", "Attribute violation",
        "{0}: {1} must have attribute {2}",
        DiagnosticSeverity.Warning, DiagnosticCategory.Structural, RuleTier.Symbol);

    /// <summary>TNG0105: a member returns the wrong type.</summary>
    public static readonly DiagnosticDescriptor ReturnsViolation = new DiagnosticDescriptor(
        "TNG0105", "Return type violation",
        "{0}: {1} must return {2}, found {3}",
        DiagnosticSeverity.Warning, DiagnosticCategory.Structural, RuleTier.Symbol);

    /// <summary>TNG0106: a constructor injects something outside the allowed set.</summary>
    public static readonly DiagnosticDescriptor InjectsViolation = new DiagnosticDescriptor(
        "TNG0106", "Constructor injection violation",
        "{0}: constructor may only inject {1}; found {2}",
        DiagnosticSeverity.Warning, DiagnosticCategory.Structural, RuleTier.Symbol);

    /// <summary>TNG0107: a property exposes a forbidden accessor.</summary>
    public static readonly DiagnosticDescriptor AccessorViolation = new DiagnosticDescriptor(
        "TNG0107", "Accessor violation",
        "{0}: property {1} must not have a public setter",
        DiagnosticSeverity.Warning, DiagnosticCategory.Structural, RuleTier.Symbol);

    /// <summary>TNG0108: the type declares a forbidden kind of member.</summary>
    public static readonly DiagnosticDescriptor MemberKindViolation = new DiagnosticDescriptor(
        "TNG0108", "Member kind violation",
        "{0}: type must not declare {1}",
        DiagnosticSeverity.Warning, DiagnosticCategory.Structural, RuleTier.Symbol);

    /// <summary>TNG0109: a companion interface lives in the wrong layer.</summary>
    public static readonly DiagnosticDescriptor InterfaceLivesInViolation = new DiagnosticDescriptor(
        "TNG0109", "Interface location violation",
        "{0}: interface {1} must live in {2}",
        DiagnosticSeverity.Warning, DiagnosticCategory.Structural, RuleTier.Symbol);

    /// <summary>TNG0110: the type implements an interface the contract does not allow.</summary>
    public static readonly DiagnosticDescriptor ImplementsOnlyViolation = new DiagnosticDescriptor(
        "TNG0110", "Interface not allowed",
        "{0}: may only implement {1}; found {2}",
        DiagnosticSeverity.Warning, DiagnosticCategory.Structural, RuleTier.Symbol);

    /// <summary>TNG0111: the type inherits a base type the contract does not allow.</summary>
    public static readonly DiagnosticDescriptor InheritsOnlyViolation = new DiagnosticDescriptor(
        "TNG0111", "Base type not allowed",
        "{0}: may only inherit {1}; found {2}",
        DiagnosticSeverity.Warning, DiagnosticCategory.Structural, RuleTier.Symbol);

    /// <summary>TNG0112: a name does not satisfy the contract's naming rule.</summary>
    public static readonly DiagnosticDescriptor NameViolation = new DiagnosticDescriptor(
        "TNG0112", "Name violation",
        "{0}: {1} must have a name that {2}",
        DiagnosticSeverity.Warning, DiagnosticCategory.Structural, RuleTier.Symbol);

    /// <summary>TNG0113: the type lives outside the layer or namespace the contract requires.</summary>
    public static readonly DiagnosticDescriptor LivesInViolation = new DiagnosticDescriptor(
        "TNG0113", "Location violation",
        "{0}: type must live in {1}, but lives in {2}",
        DiagnosticSeverity.Warning, DiagnosticCategory.Structural, RuleTier.Symbol);

    // ---- TNG02xx: reference / layering (tier 1) --------------------------

    /// <summary>TNG0201: a symbol in a forbidden namespace is referenced.</summary>
    public static readonly DiagnosticDescriptor ForbiddenNamespaceReference = new DiagnosticDescriptor(
        "TNG0201", "Forbidden namespace reference",
        "{0}: reference to {1} in forbidden namespace {2}",
        DiagnosticSeverity.Warning, DiagnosticCategory.Reference, RuleTier.Reference);

    /// <summary>TNG0202: a layer references a layer it may not depend on.</summary>
    public static readonly DiagnosticDescriptor LayerDependencyViolation = new DiagnosticDescriptor(
        "TNG0202", "Layer dependency violation",
        "Layer {0} may not depend on layer {1} ({2})",
        DiagnosticSeverity.Warning, DiagnosticCategory.Reference, RuleTier.Reference);

    /// <summary>TNG0204: a reference to a type bound to a forbidden contract.</summary>
    public static readonly DiagnosticDescriptor ForbiddenContractReference = new DiagnosticDescriptor(
        "TNG0204", "Forbidden contract reference",
        "{0}: reference to {1}, which is bound to contract {2}",
        DiagnosticSeverity.Warning, DiagnosticCategory.Reference, RuleTier.Reference);

    /// <summary>TNG0205: a reference to a namespace no layer declares and no allow list permits.</summary>
    public static readonly DiagnosticDescriptor UndeclaredDependency = new DiagnosticDescriptor(
        "TNG0205", "Undeclared external dependency",
        "Layer {0} may not depend on {1} ({2}); add it to the project's allow list",
        DiagnosticSeverity.Warning, DiagnosticCategory.Reference, RuleTier.Reference);

    /// <summary>TNG0203: a forbidden object creation expression.</summary>
    public static readonly DiagnosticDescriptor ForbiddenNewViolation = new DiagnosticDescriptor(
        "TNG0203", "Forbidden object creation",
        "{0}: forbidden object creation: new {1}",
        DiagnosticSeverity.Warning, DiagnosticCategory.Reference, RuleTier.Reference);

    // ---- TNG03xx: flow (tier 2) ------------------------------------------

    /// <summary>TNG0301: a constructor parameter is not stored in a private readonly field.</summary>
    public static readonly DiagnosticDescriptor ParameterNotAssignedToField = new DiagnosticDescriptor(
        "TNG0301", "Constructor parameter not assigned to field",
        "{0}: constructor parameter {1} is not assigned to a private readonly field",
        DiagnosticSeverity.Warning, DiagnosticCategory.Flow, RuleTier.Flow);

    /// <summary>TNG0302: a constructor parameter is not null-checked.</summary>
    public static readonly DiagnosticDescriptor ParameterNotNullChecked = new DiagnosticDescriptor(
        "TNG0302", "Constructor parameter not null-checked",
        "{0}: constructor parameter {1} is not null-checked",
        DiagnosticSeverity.Warning, DiagnosticCategory.Flow, RuleTier.Flow);

    /// <summary>TNG0303: the type holds mutable instance state.</summary>
    public static readonly DiagnosticDescriptor NotStateless = new DiagnosticDescriptor(
        "TNG0303", "Type is not stateless",
        "{0}: type must be stateless; mutable instance field {1}",
        DiagnosticSeverity.Warning, DiagnosticCategory.Flow, RuleTier.Flow);

    // ---- TNG10xx: model problems -----------------------------------------

    /// <summary>TNG1000: a lexical or syntactic error in a <c>.tan</c> file.</summary>
    public static readonly DiagnosticDescriptor ParseError = new DiagnosticDescriptor(
        "TNG1000", "Parse error",
        "{0}",
        DiagnosticSeverity.Error, DiagnosticCategory.Model);

    /// <summary>TNG1001: a <c>bind</c>, <c>ignore</c> or <c>extends</c> names a contract that does not exist.</summary>
    public static readonly DiagnosticDescriptor UnknownContract = new DiagnosticDescriptor(
        "TNG1001", "Unknown contract",
        "Unknown contract '{0}' in bind/ignore",
        DiagnosticSeverity.Error, DiagnosticCategory.Model);

    /// <summary>TNG1002: a <c>bind</c> or <c>ignore</c> names a type that does not exist in the compilation.</summary>
    public static readonly DiagnosticDescriptor UnknownType = new DiagnosticDescriptor(
        "TNG1002", "Unknown type",
        "Unknown type '{0}' in bind/ignore",
        DiagnosticSeverity.Error, DiagnosticCategory.Model);

    /// <summary>TNG1003: two declarations share a name.</summary>
    public static readonly DiagnosticDescriptor DuplicateDeclaration = new DiagnosticDescriptor(
        "TNG1003", "Duplicate declaration",
        "Duplicate declaration '{0}'",
        DiagnosticSeverity.Error, DiagnosticCategory.Model);

    /// <summary>TNG1004: contracts extend each other in a cycle.</summary>
    public static readonly DiagnosticDescriptor ContractCycle = new DiagnosticDescriptor(
        "TNG1004", "Contract inheritance cycle",
        "Contract inheritance cycle: {0}",
        DiagnosticSeverity.Error, DiagnosticCategory.Model);

    /// <summary>TNG1005: a layer name is used before it is declared.</summary>
    public static readonly DiagnosticDescriptor UndeclaredLayer = new DiagnosticDescriptor(
        "TNG1005", "Undeclared layer",
        "Undeclared layer '{0}'",
        DiagnosticSeverity.Error, DiagnosticCategory.Model);

    /// <summary>TNG1006: the declared layer dependencies contain a cycle.</summary>
    public static readonly DiagnosticDescriptor LayerCycle = new DiagnosticDescriptor(
        "TNG1006", "Layer dependency cycle",
        "Layer dependency cycle: {0}",
        DiagnosticSeverity.Warning, DiagnosticCategory.Model);

    /// <summary>TNG1010: two rules contradict each other at the same specificity.</summary>
    public static readonly DiagnosticDescriptor ConflictingRules = new DiagnosticDescriptor(
        "TNG1010", "Conflicting rules",
        "Conflicting rules at same specificity: {0} vs {1}",
        DiagnosticSeverity.Error, DiagnosticCategory.Model);

    /// <summary>TNG1011: a child contract loosens a rule it inherited.</summary>
    public static readonly DiagnosticDescriptor LoosensInheritedRule = new DiagnosticDescriptor(
        "TNG1011", "Contract loosens an inherited rule",
        "Contract '{0}' loosens rule {1} inherited from '{2}'",
        DiagnosticSeverity.Error, DiagnosticCategory.Model);

    /// <summary>TNG1020: a type named in a contract could not be resolved against the compilation.</summary>
    public static readonly DiagnosticDescriptor UnresolvedTypeName = new DiagnosticDescriptor(
        "TNG1020", "Unresolvable type name",
        "Type '{0}' in contract '{1}' could not be resolved",
        DiagnosticSeverity.Warning, DiagnosticCategory.Model);

    /// <summary>TNG1030: a contract has neither a <c>match</c> clause nor a <c>bind</c>.</summary>
    public static readonly DiagnosticDescriptor ContractNeverBound = new DiagnosticDescriptor(
        "TNG1030", "Contract is never bound",
        "Contract '{0}' is never bound",
        DiagnosticSeverity.Warning, DiagnosticCategory.Model);

    // ---- TNG9000: ignores ------------------------------------------------

    /// <summary>TNG9000: an ignore suppressed a violation.</summary>
    public static readonly DiagnosticDescriptor ViolationIgnored = new DiagnosticDescriptor(
        "TNG9000", "Violation ignored",
        "Violation ignored: {0}",
        DiagnosticSeverity.Hidden, DiagnosticCategory.Ignore);

    /// <summary>Every descriptor, in id order.</summary>
    public static readonly ImmutableArray<DiagnosticDescriptor> All = ImmutableArray.Create(
        UnknownContractAttribute,
        UnknownRuleInIgnore,
        ModifierViolation,
        InheritsViolation,
        ImplementsViolation,
        AttributeViolation,
        ReturnsViolation,
        InjectsViolation,
        AccessorViolation,
        MemberKindViolation,
        InterfaceLivesInViolation,
        ForbiddenNamespaceReference,
        LayerDependencyViolation,
        ForbiddenNewViolation,
        ImplementsOnlyViolation,
        InheritsOnlyViolation,
        NameViolation,
        LivesInViolation,
        ForbiddenContractReference,
        UndeclaredDependency,
        ParameterNotAssignedToField,
        ParameterNotNullChecked,
        NotStateless,
        ParseError,
        UnknownContract,
        UnknownType,
        DuplicateDeclaration,
        ContractCycle,
        UndeclaredLayer,
        LayerCycle,
        ConflictingRules,
        LoosensInheritedRule,
        UnresolvedTypeName,
        ContractNeverBound,
        ViolationIgnored);

    private static readonly Dictionary<string, DiagnosticDescriptor> ById = BuildIndex();

    private static Dictionary<string, DiagnosticDescriptor> BuildIndex()
    {
        var map = new Dictionary<string, DiagnosticDescriptor>(All.Length, StringComparer.Ordinal);
        for (int i = 0; i < All.Length; i++) map[All[i].Id] = All[i];
        return map;
    }

    /// <summary>Looks a descriptor up by id.</summary>
    public static bool TryGet(string id, out DiagnosticDescriptor descriptor) => ById.TryGetValue(id, out descriptor!);
}
