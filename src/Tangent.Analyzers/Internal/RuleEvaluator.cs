using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Tangent.Core.Diagnostics;
using Tangent.Core.Ir;
using Tangent.Core.Matching;
using Tangent.Core.Text;
using CoreDescriptor = Tangent.Core.Diagnostics.DiagnosticDescriptor;

namespace Tangent.Analyzers.Internal;

/// <summary>
/// Evaluates the Tier 0 rules of one bound type. A <c>ref struct</c> so the symbol, the model and the
/// diagnostic sink travel together on the stack and never escape to the heap.
/// </summary>
internal ref struct RuleEvaluator
{
    private readonly CompilationState _state;
    private readonly TypeContext _context;
    private readonly SymbolAnalysisContext _symbolContext;

    internal RuleEvaluator(CompilationState state, TypeContext context, SymbolAnalysisContext symbolContext)
    {
        _state = state;
        _context = context;
        _symbolContext = symbolContext;
    }

    private ArchitectureModel Model => _state.Model;

    private INamedTypeSymbol Type => _context.Type;

    private ReadOnlySpan<char> BoundName => Type.Name.AsSpan();

    private ReadOnlySpan<char> BoundNamespace => _context.NamespaceName.AsSpan();

    /// <summary>Runs every Tier 0 rule bound to the type, plus the symbol-derivable <c>stateless</c> rule.</summary>
    internal void Evaluate()
    {
        ImmutableArray<AppliedRule> rules = _context.SymbolRules;
        for (int i = 0; i < rules.Length; i++)
        {
            Rule rule = RuleOf(rules[i]);
            if (rule.Selector.Kind != MemberKind.Type) continue;
            if (!_state.EvaluateCondition(rule.Selector.ConditionIndex, Type, Type)) continue;
            EvaluateTypeRule(rules[i], in rule);
        }

        ImmutableArray<AppliedRule> flow = _context.FlowRules;
        for (int i = 0; i < flow.Length; i++)
        {
            Rule rule = RuleOf(flow[i]);
            if (rule.Predicate.Kind == PredicateKind.Stateless) EvaluateStateless(flow[i], in rule);
        }

        EvaluateMembers();
    }

    private Rule RuleOf(AppliedRule applied) => Model.Contracts[applied.ContractIndex].Rules[applied.RuleIndex];

    private string ContractName(AppliedRule applied) => Model.Text(Model.Contracts[applied.ContractIndex].Name);

    // ------------------------------------------------------------------
    // type-level rules
    // ------------------------------------------------------------------

    private void EvaluateTypeRule(AppliedRule applied, in Rule rule)
    {
        switch (rule.Predicate.Kind)
        {
            case PredicateKind.Modifier:
                EvaluateModifier(applied, in rule, Type, MemberKind.Type, SymbolFacts.LocationOf(Type));
                return;

            case PredicateKind.Inherits:
            {
                bool holds = PatternMatching.AnyBaseMatches(Model, rule.Predicate.Payload, Type, BoundName, BoundNamespace);
                if (holds == (rule.Polarity == Polarity.Require)) return;
                Report(
                    applied, in rule, DiagnosticCatalog.InheritsViolation, SymbolFacts.LocationOf(Type),
                    string.Empty, string.Empty,
                    rule.Polarity == Polarity.Require ? FixKinds.AddBaseType : null,
                    SingleConcreteName(rule.Predicate.Payload),
                    ContractName(applied), Negate(in rule, DescribeExpectedGroup(rule.Predicate.Payload)));
                return;
            }

            case PredicateKind.Implements:
            {
                bool holds = PatternMatching.AnyInterfaceMatches(Model, rule.Predicate.Payload, Type, BoundName, BoundNamespace);
                if (holds == (rule.Polarity == Polarity.Require)) return;
                Report(
                    applied, in rule, DiagnosticCatalog.ImplementsViolation, SymbolFacts.LocationOf(Type),
                    string.Empty, string.Empty,
                    rule.Polarity == Polarity.Require ? FixKinds.AddInterface : null,
                    SingleConcreteName(rule.Predicate.Payload),
                    ContractName(applied), Negate(in rule, DescribeExpectedGroup(rule.Predicate.Payload)));
                return;
            }

            case PredicateKind.ImplementsOnly:
                EvaluateImplementsOnly(applied, in rule);
                return;

            case PredicateKind.InheritsOnly:
                EvaluateInheritsOnly(applied, in rule);
                return;

            case PredicateKind.NameIs:
                EvaluateName(applied, in rule, Type, MemberKind.Type, SymbolFacts.LocationOf(Type));
                return;

            case PredicateKind.LivesIn:
                EvaluateLivesIn(applied, in rule);
                return;

            case PredicateKind.HasAttributes:
                EvaluateAttributes(applied, in rule, Type, MemberKind.Type, SymbolFacts.LocationOf(Type));
                return;

            case PredicateKind.HasMemberKind:
                EvaluateMemberKind(applied, in rule);
                return;

            case PredicateKind.InterfaceLivesIn:
                EvaluateInterfaceLivesIn(applied, in rule);
                return;

            default:
                return;
        }
    }

    private void EvaluateMemberKind(AppliedRule applied, in Rule rule)
    {
        var kind = (MemberKind)rule.Predicate.Payload;
        var mask = (Modifiers)rule.Predicate.Payload2;
        string what = mask == Modifiers.None
            ? SymbolFacts.MemberKindNoun(kind)
            : IrText.ModifierText(mask) + " " + SymbolFacts.MemberKindNoun(kind);

        ImmutableArray<ISymbol> members = Type.GetMembers();
        bool found = false;

        for (int i = 0; i < members.Length; i++)
        {
            ISymbol member = members[i];
            if (SymbolFacts.ClassifyMember(member) != kind) continue;
            if (!SymbolFacts.HasModifiers(member, mask)) continue;
            found = true;
            if (rule.Polarity != Polarity.Forbid) break;
            if (IsMemberIgnored(member, applied, in rule)) continue;

            Report(
                applied, in rule, DiagnosticCatalog.MemberKindViolation, SymbolFacts.LocationOf(member),
                string.Empty, member.Name,
                mask == Modifiers.None ? null : FixKinds.RemoveModifier,
                mask == Modifiers.None ? null : IrText.ModifierText(mask),
                ContractName(applied), what);
        }

        if (rule.Polarity == Polarity.Require && !found)
        {
            ReportRaw(
                applied, in rule, DiagnosticCatalog.MemberKindViolation.Id, SymbolFacts.LocationOf(Type),
                string.Empty, null, null,
                ContractName(applied) + ": type must declare " + what +
                ". (rule " + rule.Id.Value + ", " + MessageFactory.Origin(Model, rule.Span) + ")");
        }
    }

    private void EvaluateInterfaceLivesIn(AppliedRule applied, in Rule rule)
    {
        int templateIndex = rule.Predicate.Payload;
        if (templateIndex < 0 || templateIndex >= Model.Templates.Length) return;

        string expected = Model.Templates[templateIndex].Expand(Type.Name, _context.NamespaceName);
        int layerIndex = rule.Predicate.Payload2;
        string layerName = _state.LayerName(layerIndex);

        INamedTypeSymbol? companion = _state.FindInterface(Type, expected);
        bool holds = companion is not null &&
                     layerIndex >= 0 &&
                     _state.GetNamespaceInfo(companion.ContainingNamespace).LayerIndex == layerIndex;

        if (holds == (rule.Polarity == Polarity.Require)) return;

        Report(
            applied, in rule, DiagnosticCatalog.InterfaceLivesInViolation, SymbolFacts.LocationOf(Type),
            string.Empty, string.Empty, null, null,
            ContractName(applied), expected, layerName.Length == 0 ? "the declared layer" : layerName);
    }

    private void EvaluateStateless(AppliedRule applied, in Rule rule)
    {
        if (rule.Polarity != Polarity.Require) return;

        ImmutableArray<ISymbol> members = Type.GetMembers();
        for (int i = 0; i < members.Length; i++)
        {
            if (members[i] is not IFieldSymbol field) continue;
            if (field.IsStatic || field.IsConst || field.IsReadOnly) continue;

            if (field.AssociatedSymbol is IPropertySymbol owner)
            {
                if (owner.SetMethod is null || owner.SetMethod.IsInitOnly) continue;
                if (IsMemberIgnored(owner, applied, in rule)) continue;
                Report(
                    applied, in rule, DiagnosticCatalog.NotStateless, SymbolFacts.LocationOf(owner),
                    string.Empty, owner.Name, null, null,
                    ContractName(applied), "backing property '" + owner.Name + "'");
                continue;
            }

            if (field.IsImplicitlyDeclared) continue;
            if (IsMemberIgnored(field, applied, in rule)) continue;
            Report(
                applied, in rule, DiagnosticCatalog.NotStateless, SymbolFacts.LocationOf(field),
                string.Empty, field.Name, FixKinds.AddModifier, "readonly",
                ContractName(applied), "'" + field.Name + "'");
        }
    }

    // ------------------------------------------------------------------
    // member-level rules
    // ------------------------------------------------------------------

    private void EvaluateMembers()
    {
        ImmutableArray<AppliedRule> rules = _context.SymbolRules;
        bool anyMemberRule = false;
        for (int i = 0; i < rules.Length; i++)
        {
            if (RuleOf(rules[i]).Selector.Kind != MemberKind.Type)
            {
                anyMemberRule = true;
                break;
            }
        }

        if (!anyMemberRule) return;

        ImmutableArray<ISymbol> members = Type.GetMembers();
        for (int m = 0; m < members.Length; m++)
        {
            ISymbol member = members[m];
            MemberKind? kind = SymbolFacts.ClassifyMember(member);
            if (kind is null || kind.Value == MemberKind.Type) continue;

            for (int i = 0; i < rules.Length; i++)
            {
                Rule rule = RuleOf(rules[i]);
                if (rule.Selector.Kind != kind.Value) continue;
                if (!_state.EvaluateCondition(rule.Selector.ConditionIndex, member, Type)) continue;
                if (IsMemberIgnored(member, rules[i], in rule)) continue;
                EvaluateMemberRule(rules[i], in rule, member, kind.Value);
            }
        }
    }

    private void EvaluateMemberRule(AppliedRule applied, in Rule rule, ISymbol member, MemberKind kind)
    {
        Location location = SymbolFacts.LocationOf(member);
        switch (rule.Predicate.Kind)
        {
            case PredicateKind.Modifier:
                EvaluateModifier(applied, in rule, member, kind, location);
                return;

            case PredicateKind.NameIs:
                EvaluateName(applied, in rule, member, kind, location);
                return;

            case PredicateKind.HasAttributes:
                EvaluateAttributes(applied, in rule, member, kind, location);
                return;

            case PredicateKind.Returns:
                EvaluateReturns(applied, in rule, member, kind, location);
                return;

            case PredicateKind.Injects:
            case PredicateKind.InjectsOnly:
                EvaluateInjects(applied, in rule, member, location);
                return;

            case PredicateKind.Accessor:
                EvaluateAccessor(applied, in rule, member, location);
                return;

            case PredicateKind.Implements:
            {
                if (member is not INamedTypeSymbol nested) return;
                bool holds = PatternMatching.AnyInterfaceMatches(Model, rule.Predicate.Payload, nested, BoundName, BoundNamespace);
                if (holds == (rule.Polarity == Polarity.Require)) return;
                Report(
                    applied, in rule, DiagnosticCatalog.ImplementsViolation, location,
                    SymbolFacts.DescribeMember(member, kind), member.Name, null, null,
                    ContractName(applied), Negate(in rule, Model.TypePatterns.DescribeGroup(rule.Predicate.Payload)));
                return;
            }

            case PredicateKind.Inherits:
            {
                if (member is not INamedTypeSymbol nested) return;
                bool holds = PatternMatching.AnyBaseMatches(Model, rule.Predicate.Payload, nested, BoundName, BoundNamespace);
                if (holds == (rule.Polarity == Polarity.Require)) return;
                Report(
                    applied, in rule, DiagnosticCatalog.InheritsViolation, location,
                    SymbolFacts.DescribeMember(member, kind), member.Name, null, null,
                    ContractName(applied), Negate(in rule, Model.TypePatterns.DescribeGroup(rule.Predicate.Payload)));
                return;
            }

            default:
                return;
        }
    }

    private void EvaluateModifier(AppliedRule applied, in Rule rule, ISymbol symbol, MemberKind kind, Location location)
    {
        var mask = (Modifiers)rule.Predicate.Payload;

        // `const` fields can never be readonly; a readonly rule does not address them.
        if ((mask & Modifiers.Readonly) != 0 && symbol is IFieldSymbol { IsConst: true }) return;

        bool holds = SymbolFacts.HasModifiers(symbol, mask);
        if (holds == (rule.Polarity == Polarity.Require)) return;

        string text = IrText.ModifierText(mask);
        string? fix = (mask & Modifiers.TypeKindMask) != 0
            ? null
            : rule.Polarity == Polarity.Require ? FixKinds.AddModifier : FixKinds.RemoveModifier;

        Report(
            applied, in rule, DiagnosticCatalog.ModifierViolation, location,
            kind == MemberKind.Type ? string.Empty : SymbolFacts.DescribeMember(symbol, kind),
            kind == MemberKind.Type ? string.Empty : symbol.Name,
            fix, text,
            ContractName(applied), rule.Polarity == Polarity.Require ? "be " + text : "not be " + text);
    }

    private void EvaluateAttributes(AppliedRule applied, in Rule rule, ISymbol symbol, MemberKind kind, Location location)
    {
        bool holds = PatternMatching.HasAttribute(Model, rule.Predicate.Payload, symbol);
        if (holds == (rule.Polarity == Polarity.Require)) return;

        AttributeGroup group = Model.AttributeGroups[rule.Predicate.Payload];
        string describe = group.Describe();

        Report(
            applied, in rule, DiagnosticCatalog.AttributeViolation, location, string.Empty,
            kind == MemberKind.Type ? string.Empty : symbol.Name,
            rule.Polarity == Polarity.Require ? FixKinds.AddAttribute : null,
            rule.Polarity == Polarity.Require ? FirstAlternative(group) : null,
            ContractName(applied),
            kind == MemberKind.Type ? "type" : SymbolFacts.DescribeMember(symbol, kind),
            rule.Polarity == Polarity.Require ? describe : "no " + describe);
    }

    private void EvaluateReturns(AppliedRule applied, in Rule rule, ISymbol member, MemberKind kind, Location location)
    {
        ITypeSymbol? returnType = member switch
        {
            IMethodSymbol method => method.ReturnType,
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            IEventSymbol @event => @event.Type,
            _ => null,
        };

        if (returnType is null) return;

        bool holds = PatternMatching.MatchesGroup(Model, rule.Predicate.Payload, returnType, BoundName, BoundNamespace);
        if (holds == (rule.Polarity == Polarity.Require)) return;

        string expected = Model.TypePatterns.DescribeGroup(rule.Predicate.Payload);
        Report(
            applied, in rule, DiagnosticCatalog.ReturnsViolation, location, string.Empty, member.Name, null, null,
            ContractName(applied),
            SymbolFacts.DescribeMember(member, kind),
            rule.Polarity == Polarity.Require ? expected : "anything but " + expected,
            returnType.ToDisplayString());
    }

    private void EvaluateInjects(AppliedRule applied, in Rule rule, ISymbol member, Location location)
    {
        if (member is not IMethodSymbol constructor) return;
        ImmutableArray<IParameterSymbol> parameters = constructor.Parameters;
        string expected = Model.TypePatterns.DescribeGroup(rule.Predicate.Payload);

        if (rule.Predicate.Kind == PredicateKind.InjectsOnly)
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                bool matches = PatternMatching.MatchesGroup(Model, rule.Predicate.Payload, parameters[i].Type, BoundName, BoundNamespace);
                if (matches == (rule.Polarity == Polarity.Require)) continue;
                Report(
                    applied, in rule, DiagnosticCatalog.InjectsViolation,
                    SymbolFacts.LocationOf(parameters[i]), string.Empty, parameters[i].Name, null, null,
                    ContractName(applied), expected,
                    parameters[i].Type.ToDisplayString() + " " + parameters[i].Name);
            }

            return;
        }

        bool found = false;
        for (int i = 0; i < parameters.Length && !found; i++)
        {
            found = PatternMatching.MatchesGroup(Model, rule.Predicate.Payload, parameters[i].Type, BoundName, BoundNamespace);
        }

        if (found == (rule.Polarity == Polarity.Require)) return;

        Report(
            applied, in rule, DiagnosticCatalog.InjectsViolation, location, string.Empty, member.Name, null, null,
            ContractName(applied),
            rule.Polarity == Polarity.Require ? expected : "anything but " + expected,
            parameters.Length == 0 ? "no parameters" : DescribeParameters(parameters));
    }

    private void EvaluateAccessor(AppliedRule applied, in Rule rule, ISymbol member, Location location)
    {
        if (member is not IPropertySymbol property) return;

        var accessor = (AccessorKind)rule.Predicate.Payload;
        var mask = (Modifiers)rule.Predicate.Payload2;

        IMethodSymbol? method;
        bool holds;
        switch (accessor)
        {
            case AccessorKind.Get:
                method = property.GetMethod;
                holds = method is not null && (mask == Modifiers.None || SymbolFacts.HasModifiers(method, mask));
                break;
            case AccessorKind.Init:
                method = property.SetMethod;
                holds = method is { IsInitOnly: true } && (mask == Modifiers.None || SymbolFacts.HasModifiers(method, mask));
                break;
            default:
                method = property.SetMethod;
                holds = method is { IsInitOnly: false } && (mask == Modifiers.None || SymbolFacts.HasModifiers(method, mask));
                break;
        }

        if (holds == (rule.Polarity == Polarity.Require)) return;

        bool canonical = rule.Polarity == Polarity.Forbid && accessor == AccessorKind.Set && (mask & Modifiers.Public) != 0;
        if (canonical)
        {
            Report(
                applied, in rule, DiagnosticCatalog.AccessorViolation,
                method is not null ? SymbolFacts.LocationOf(method) : location,
                string.Empty, property.Name, FixKinds.SetterToInit, null,
                ContractName(applied), property.Name);
            return;
        }

        string accessorText = (mask == Modifiers.None ? string.Empty : IrText.ModifierText(mask) + " ") + IrText.AccessorName(accessor);
        ReportRaw(
            applied, in rule, DiagnosticCatalog.AccessorViolation.Id, location, property.Name, null, null,
            ContractName(applied) + ": property '" + property.Name + "' must " +
            (rule.Polarity == Polarity.Require ? "declare " : "not declare ") + "a " + accessorText + " accessor" +
            ". (rule " + rule.Id.Value + ", " + MessageFactory.Origin(Model, rule.Span) + ")");
    }

    // ------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------

    private static string Negate(in Rule rule, string text) =>
        rule.Polarity == Polarity.Require ? text : "anything but " + text;

    private static string DescribeParameters(ImmutableArray<IParameterSymbol> parameters)
    {
        var builder = new StringBuilder();
        for (int i = 0; i < parameters.Length; i++)
        {
            if (i > 0) builder.Append(", ");
            builder.Append(parameters[i].Type.ToDisplayString());
        }

        return builder.ToString();
    }

    private static string FirstAlternative(AttributeGroup group)
    {
        if (group.Alternatives.Length == 0) return string.Empty;
        string text = group.Describe();
        text = text.Substring(1, text.Length - 2);
        int bar = text.IndexOf('|');
        if (bar >= 0) text = text.Substring(0, bar);
        return text.Trim();
    }

    /// <summary>
    /// Renders a type-pattern group for a message. A templated pattern such as <c>I{Name}</c> is expanded
    /// against the bound type first, so the reader sees <c>IOrderService</c> and not the template source.
    /// </summary>
    private string DescribeExpectedGroup(int groupIndex)
    {
        ImmutableArray<int> group = Model.TypePatterns.Group(groupIndex);
        var builder = new StringBuilder();
        for (int i = 0; i < group.Length; i++)
        {
            if (i > 0) builder.Append(" | ");
            TypePattern pattern = Model.TypePatterns.Patterns[group[i]];
            if (pattern.TemplateIndex >= 0 && pattern.Arity == 0)
            {
                builder.Append(Model.Templates[pattern.TemplateIndex].Expand(Type.Name, _context.NamespaceName));
            }
            else
            {
                builder.Append(Model.TypePatterns.Describe(group[i]));
            }
        }

        return builder.ToString();
    }

    private string? SingleConcreteName(int groupIndex)
    {
        ImmutableArray<int> group = Model.TypePatterns.Group(groupIndex);
        if (group.Length != 1) return null;
        TypePattern pattern = Model.TypePatterns.Patterns[group[0]];
        if (pattern.IsWildcard || pattern.Arity != 0) return null;
        if (pattern.TemplateIndex >= 0) return Model.Templates[pattern.TemplateIndex].Expand(Type.Name, _context.NamespaceName);
        string name = Model.Text(pattern.Name);
        int dot = name.LastIndexOf('.');
        return dot < 0 ? name : name.Substring(dot + 1);
    }

    private bool IsMemberIgnored(ISymbol member, AppliedRule applied, in Rule rule)
    {
        ImmutableArray<AttributeData> attributes = member.GetAttributes();
        if (attributes.Length == 0) return false;

        string contract = ContractName(applied);
        for (int i = 0; i < attributes.Length; i++)
        {
            AttributeData attribute = attributes[i];
            if (!SymbolFacts.IsTangentIgnoreAttribute(attribute.AttributeClass)) continue;

            string? scopedContract = null;
            string? scopedRule = null;
            ImmutableArray<KeyValuePair<string, TypedConstant>> named = attribute.NamedArguments;
            for (int n = 0; n < named.Length; n++)
            {
                if (string.Equals(named[n].Key, "Contract", StringComparison.Ordinal)) scopedContract = named[n].Value.Value as string;
                else if (string.Equals(named[n].Key, "Rule", StringComparison.Ordinal)) scopedRule = named[n].Value.Value as string;
            }

            if (scopedRule is not null && !string.Equals(scopedRule, rule.Id.Value, StringComparison.Ordinal)) continue;
            if (scopedContract is not null && !string.Equals(scopedContract, contract, StringComparison.Ordinal)) continue;

            string reason = attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is string text
                ? text
                : string.Empty;

            if (_state.ShouldNoteIgnore(member))
            {
                _symbolContext.ReportDiagnostic(Diagnostic.Create(
                    TangentDiagnostics.Get(DiagnosticCatalog.ViolationIgnored.Id),
                    SymbolFacts.LocationOf(member, attribute),
                    MessageFactory.Properties(contract, rule.Id.Value, member.Name, rule.Tier),
                    DiagnosticCatalog.ViolationIgnored.Format(reason)));
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// <c>implements only X, Y</c>: every interface the type implements directly must match the group.
    /// Interfaces the group's own members inherit are not reported, so `implements only I{Name}` does not
    /// trip over what I{Name} itself extends.
    /// </summary>
    private void EvaluateImplementsOnly(AppliedRule applied, in Rule rule)
    {
        ImmutableArray<INamedTypeSymbol> interfaces = Type.Interfaces;
        if (interfaces.IsDefaultOrEmpty) return;

        var allowed = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        for (int i = 0; i < interfaces.Length; i++)
        {
            if (!PatternMatching.MatchesGroup(Model, rule.Predicate.Payload, interfaces[i], BoundName, BoundNamespace)) continue;
            allowed.Add(interfaces[i]);
            foreach (INamedTypeSymbol inherited in interfaces[i].AllInterfaces) allowed.Add(inherited);
        }

        for (int i = 0; i < interfaces.Length; i++)
        {
            INamedTypeSymbol candidate = interfaces[i];
            if (allowed.Contains(candidate)) continue;
            if (rule.Polarity != Polarity.Require) continue;

            Report(
                applied, in rule, DiagnosticCatalog.ImplementsOnlyViolation, SymbolFacts.LocationOf(Type),
                string.Empty, string.Empty, null, null,
                ContractName(applied),
                Model.TypePatterns.DescribeGroup(rule.Predicate.Payload),
                candidate.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
        }
    }

    /// <summary><c>inherits only X</c>: the base type, when there is one other than object, must match.</summary>
    private void EvaluateInheritsOnly(AppliedRule applied, in Rule rule)
    {
        INamedTypeSymbol? baseType = Type.BaseType;
        if (baseType is null || baseType.SpecialType is SpecialType.System_Object or SpecialType.System_ValueType or SpecialType.System_Enum) return;
        if (rule.Polarity != Polarity.Require) return;
        if (PatternMatching.MatchesGroupIncludingBases(Model, rule.Predicate.Payload, baseType, BoundName, BoundNamespace)) return;

        Report(
            applied, in rule, DiagnosticCatalog.InheritsOnlyViolation, SymbolFacts.LocationOf(Type),
            string.Empty, string.Empty, null, null,
            ContractName(applied),
            Model.TypePatterns.DescribeGroup(rule.Predicate.Payload),
            baseType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
    }

    /// <summary><c>name ends with "Handler"</c> and friends, on the type or on a member.</summary>
    private void EvaluateName(AppliedRule applied, in Rule rule, ISymbol symbol, MemberKind kind, Location location)
    {
        var op = (NameOperator)rule.Predicate.Payload;
        ReadOnlySpan<char> operand = Model.Strings.GetSpan(new StringId(rule.Predicate.Payload2));
        ReadOnlySpan<char> name = symbol.Name.AsSpan();

        bool holds;
        switch (op)
        {
            case NameOperator.EndsWith:
                holds = name.EndsWith(operand, StringComparison.Ordinal);
                break;
            case NameOperator.Contains:
                holds = name.IndexOf(operand, StringComparison.Ordinal) >= 0;
                break;
            case NameOperator.Matches:
                holds = _state.GetRegex(Model.Strings.GetString(new StringId(rule.Predicate.Payload2))).IsMatch(symbol.Name);
                break;
            default:
                holds = name.StartsWith(operand, StringComparison.Ordinal);
                break;
        }

        if (holds == (rule.Polarity == Polarity.Require)) return;

        string subject = kind == MemberKind.Type ? "type" : SymbolFacts.DescribeMember(symbol, kind);
        Report(
            applied, in rule, DiagnosticCatalog.NameViolation, location,
            kind == MemberKind.Type ? string.Empty : subject,
            kind == MemberKind.Type ? string.Empty : symbol.Name,
            null, null,
            ContractName(applied),
            subject,
            Negate(in rule, IrText.NameOperatorText(op) + " \"" + Model.Strings.GetString(new StringId(rule.Predicate.Payload2)) + "\""));
    }

    /// <summary><c>lives in Application</c> or <c>lives in Orders.Application.**</c>.</summary>
    private void EvaluateLivesIn(AppliedRule applied, in Rule rule)
    {
        ReadOnlySpan<char> ns = BoundNamespace;
        bool holds;

        if (rule.Predicate.Payload2 >= 0 && rule.Predicate.Payload2 < Model.Globs.Length)
        {
            holds = Model.Globs[rule.Predicate.Payload2].Matches(ns);
        }
        else if (rule.Predicate.Payload >= 0 && rule.Predicate.Payload < Model.Layers.Length)
        {
            holds = Model.Globs[Model.Layers[rule.Predicate.Payload].GlobIndex].Matches(ns);
        }
        else
        {
            return;
        }

        if (holds == (rule.Polarity == Polarity.Require)) return;

        Report(
            applied, in rule, DiagnosticCatalog.LivesInViolation, SymbolFacts.LocationOf(Type),
            string.Empty, string.Empty, null, null,
            ContractName(applied),
            Negate(in rule, IrText.DescribeLocation(Model, rule.Predicate)),
            ns.Length == 0 ? "the global namespace" : ns.ToString());
    }

    private void Report(
        AppliedRule applied,
        in Rule rule,
        CoreDescriptor descriptor,
        Location location,
        string memberDescription,
        string memberName,
        string? fix,
        string? fixArgument,
        params object?[] arguments)
    {
        string message = MessageFactory.Violation(descriptor, Model, in rule, memberDescription, arguments);
        _symbolContext.ReportDiagnostic(MessageFactory.Create(
            descriptor.Id,
            location,
            message,
            MessageFactory.Properties(ContractName(applied), rule.Id.Value, memberName, rule.Tier, fix, fixArgument)));
    }

    private void ReportRaw(
        AppliedRule applied,
        in Rule rule,
        string code,
        Location location,
        string memberName,
        string? fix,
        string? fixArgument,
        string message) =>
        _symbolContext.ReportDiagnostic(MessageFactory.Create(
            code,
            location,
            message,
            MessageFactory.Properties(ContractName(applied), rule.Id.Value, memberName, rule.Tier, fix, fixArgument)));
}
