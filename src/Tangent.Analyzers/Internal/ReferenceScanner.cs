using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Tangent.Core.Diagnostics;
using Tangent.Core.Ir;
using Tangent.Core.Matching;

namespace Tangent.Analyzers.Internal;

/// <summary>
/// The Tier 1 walk: every observed type-to-type reference is checked against the <c>forbid using</c>
/// rules of the referencing type's contracts and against the layer dependency matrix, then handed to the
/// reference sink. Shared by the symbol action (base types, interfaces, attributes, member signatures)
/// and the operation actions (invocations, member accesses, object creation, <c>typeof</c>).
/// </summary>
internal static class ReferenceScanner
{
    private const int MaxTypeArgumentDepth = 4;

    /// <summary>
    /// The dedupe scope for the symbol-level walk. Signatures and bodies are deduped independently so that
    /// the result never depends on whether the symbol action or the operation action ran first.
    /// </summary>
    private const string SignatureScope = "sig:";

    /// <summary>Observes one referenced type, recursing into its generic arguments.</summary>
    internal static void Observe(
        CompilationState state,
        TypeContext context,
        ImmutableArray<AppliedRule> usingRules,
        ITypeSymbol? referenced,
        ISymbol? referencedSymbol,
        ReferenceKind kind,
        Location location,
        string memberName,
        string scope,
        in DiagnosticReporter reporter) =>
        ObserveCore(state, context, usingRules, referenced, referencedSymbol, kind, location, memberName, scope, 0, in reporter);

    private static void ObserveCore(
        CompilationState state,
        TypeContext context,
        ImmutableArray<AppliedRule> usingRules,
        ITypeSymbol? referenced,
        ISymbol? referencedSymbol,
        ReferenceKind kind,
        Location location,
        string memberName,
        string scope,
        int depth,
        in DiagnosticReporter reporter)
    {
        if (referenced is null || depth > MaxTypeArgumentDepth) return;

        switch (referenced)
        {
            case IArrayTypeSymbol array:
                ObserveCore(state, context, usingRules, array.ElementType, null, kind, location, memberName, scope, depth + 1, in reporter);
                return;
            case IPointerTypeSymbol pointer:
                ObserveCore(state, context, usingRules, pointer.PointedAtType, null, kind, location, memberName, scope, depth + 1, in reporter);
                return;
            case ITypeParameterSymbol:
            case IDynamicTypeSymbol:
                return;
        }

        if (referenced is not INamedTypeSymbol named) return;

        ImmutableArray<ITypeSymbol> arguments = named.TypeArguments;
        for (int i = 0; i < arguments.Length; i++)
        {
            ObserveCore(state, context, usingRules, arguments[i], null, kind, location, memberName, scope, depth + 1, in reporter);
        }

        INamedTypeSymbol target = named.OriginalDefinition;
        if (SymbolEqualityComparer.Default.Equals(target, context.Type)) return;

        NamespaceInfo ns = state.GetNamespaceInfo(target.ContainingNamespace);
        if (ns.Name.Length == 0) return;

        ArchitectureModel model = state.Model;

        for (int i = 0; i < usingRules.Length; i++)
        {
            Rule rule = model.Contracts[usingRules[i].ContractIndex].Rules[usingRules[i].RuleIndex];
            if (rule.Predicate.Kind != PredicateKind.UsingNamespace) continue;
            int globIndex = rule.Predicate.Payload;
            if (globIndex < 0 || globIndex >= model.Globs.Length) continue;
            // `using` rules are prohibitions: a reference into a matching namespace is the violation.
            if (rule.Polarity != Polarity.Forbid) continue;
            Glob glob = model.Globs[globIndex];
            if (!glob.Matches(ns.Name.AsSpan())) continue;

            string contract = model.Text(model.Contracts[usingRules[i].ContractIndex].Name);
            if (!state.ShouldReport(context.Type, scope, target, DiagnosticCatalog.ForbiddenNamespaceReference.Id, rule.Id.Value)) continue;

            string symbolName = referencedSymbol is null ? target.Name : referencedSymbol.Name;
            reporter.Report(MessageFactory.Create(
                DiagnosticCatalog.ForbiddenNamespaceReference.Id,
                location,
                MessageFactory.Violation(
                    DiagnosticCatalog.ForbiddenNamespaceReference, model, in rule, string.Empty,
                    contract, symbolName, glob.Text),
                MessageFactory.Properties(contract, rule.Id.Value, memberName, rule.Tier)));
        }

        bool violatesLayering = false;
        if (context.LayerIndex >= 0 && !model.IsReferenceAllowed(context.LayerIndex, ns.Name.AsSpan()))
        {
            violatesLayering = true;
            if (state.ShouldReport(context.Type, scope, target, DiagnosticCatalog.LayerDependencyViolation.Id, string.Empty))
            {
                string fromLayer = state.LayerName(context.LayerIndex);
                string toLayer = state.LayerName(ns.LayerIndex);
                string symbolName = referencedSymbol is null ? target.Name : referencedSymbol.Name;
                reporter.Report(MessageFactory.Create(
                    DiagnosticCatalog.LayerDependencyViolation.Id,
                    location,
                    DiagnosticCatalog.LayerDependencyViolation.Format(fromLayer, toLayer, symbolName) +
                    ". (rule layer:" + fromLayer + "->" + toLayer + ", " +
                    MessageFactory.Origin(model, model.Layers[context.LayerIndex].DeclarationSpan) + ")",
                    LayerProperties(fromLayer, toLayer, symbolName, memberName)));
            }
        }

        state.Sink?.OnReference(context.Type, target, kind, location, violatesLayering);
    }

    private static ImmutableDictionary<string, string?> LayerProperties(
        string fromLayer,
        string toLayer,
        string symbol,
        string member)
    {
        ImmutableDictionary<string, string?>.Builder builder =
            ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
        builder[MessageFactory.ContractProperty] = string.Empty;
        builder[MessageFactory.RuleProperty] = "layer:" + fromLayer + "->" + toLayer;
        builder[MessageFactory.MemberProperty] = member;
        builder[MessageFactory.TierProperty] = IrText.TierName(RuleTier.Reference);
        builder[MessageFactory.FromLayerProperty] = fromLayer;
        builder[MessageFactory.ToLayerProperty] = toLayer;
        builder[MessageFactory.SymbolProperty] = symbol;
        return builder.ToImmutable();
    }

    /// <summary>Walks a type's own signature surface: base type, interfaces, attributes and member signatures.</summary>
    internal static void ScanSymbol(
        CompilationState state,
        TypeContext context,
        ImmutableArray<AppliedRule> usingRules,
        in DiagnosticReporter reporter)
    {
        INamedTypeSymbol type = context.Type;
        Location typeLocation = SymbolFacts.LocationOf(type);

        if (type.BaseType is not null && type.BaseType.SpecialType != SpecialType.System_Object)
        {
            Observe(state, context, usingRules, type.BaseType, null, ReferenceKind.BaseType, typeLocation, string.Empty, SignatureScope, in reporter);
        }

        ImmutableArray<INamedTypeSymbol> interfaces = type.Interfaces;
        for (int i = 0; i < interfaces.Length; i++)
        {
            Observe(state, context, usingRules, interfaces[i], null, ReferenceKind.Interface, typeLocation, string.Empty, SignatureScope, in reporter);
        }

        // The type's own attributes are the only ones the operation walk never sees: a member's attribute
        // arguments arrive as an operation block, a type's do not.
        ScanAttributes(state, context, usingRules, type, typeLocation, string.Empty, true, in reporter);

        ImmutableArray<ISymbol> members = type.GetMembers();
        for (int i = 0; i < members.Length; i++)
        {
            ISymbol member = members[i];
            if (member.IsImplicitlyDeclared) continue;
            Location location = SymbolFacts.LocationOf(member);
            if (location == Location.None) continue;
            string name = member.Name;

            ScanAttributes(state, context, usingRules, member, location, name, false, in reporter);

            switch (member)
            {
                case IMethodSymbol method:
                {
                    if (method.MethodKind != MethodKind.Ordinary && method.MethodKind != MethodKind.Constructor) break;
                    Observe(state, context, usingRules, method.ReturnType, null, ReferenceKind.MemberSignature, location, name, SignatureScope + name, in reporter);
                    ImmutableArray<IParameterSymbol> parameters = method.Parameters;
                    for (int p = 0; p < parameters.Length; p++)
                    {
                        Observe(state, context, usingRules, parameters[p].Type, null, ReferenceKind.MemberSignature,
                            SymbolFacts.LocationOf(parameters[p]), name, SignatureScope + name, in reporter);
                    }

                    break;
                }

                case IPropertySymbol property:
                    Observe(state, context, usingRules, property.Type, null, ReferenceKind.MemberSignature, location, name, SignatureScope + name, in reporter);
                    break;

                case IFieldSymbol field:
                    Observe(state, context, usingRules, field.Type, null, ReferenceKind.MemberSignature, location, name, SignatureScope + name, in reporter);
                    break;

                case IEventSymbol @event:
                    Observe(state, context, usingRules, @event.Type, null, ReferenceKind.MemberSignature, location, name, SignatureScope + name, in reporter);
                    break;
            }
        }
    }

    private static void ScanAttributes(
        CompilationState state,
        TypeContext context,
        ImmutableArray<AppliedRule> usingRules,
        ISymbol symbol,
        Location fallback,
        string memberName,
        bool scanTypeArguments,
        in DiagnosticReporter reporter)
    {
        ImmutableArray<AttributeData> attributes = symbol.GetAttributes();
        for (int i = 0; i < attributes.Length; i++)
        {
            INamedTypeSymbol? attributeClass = attributes[i].AttributeClass;
            if (attributeClass is null) continue;
            Location location = SymbolFacts.LocationOf(symbol, attributes[i]) is { } candidate && candidate != Location.None
                ? candidate
                : fallback;
            Observe(state, context, usingRules, attributeClass, null, ReferenceKind.Attribute, location,
                memberName, SignatureScope + memberName, in reporter);

            if (!scanTypeArguments) continue;

            // `[Model(typeof(ForbiddenType))]` names a type as surely as the attribute itself does.
            ScanTypedConstants(state, context, usingRules, attributes[i].ConstructorArguments, location, memberName, in reporter);
            ImmutableArray<KeyValuePair<string, TypedConstant>> named = attributes[i].NamedArguments;
            for (int n = 0; n < named.Length; n++)
            {
                ScanTypedConstant(state, context, usingRules, named[n].Value, location, memberName, in reporter);
            }
        }
    }

    private static void ScanTypedConstants(
        CompilationState state,
        TypeContext context,
        ImmutableArray<AppliedRule> usingRules,
        ImmutableArray<TypedConstant> constants,
        Location location,
        string memberName,
        in DiagnosticReporter reporter)
    {
        for (int i = 0; i < constants.Length; i++)
        {
            ScanTypedConstant(state, context, usingRules, constants[i], location, memberName, in reporter);
        }
    }

    private static void ScanTypedConstant(
        CompilationState state,
        TypeContext context,
        ImmutableArray<AppliedRule> usingRules,
        TypedConstant constant,
        Location location,
        string memberName,
        in DiagnosticReporter reporter)
    {
        if (constant.Kind == TypedConstantKind.Type)
        {
            Observe(state, context, usingRules, constant.Value as ITypeSymbol, null, ReferenceKind.TypeOf, location,
                memberName, SignatureScope + memberName, in reporter);
            return;
        }

        if (constant.Kind == TypedConstantKind.Array && !constant.Values.IsDefaultOrEmpty)
        {
            ScanTypedConstants(state, context, usingRules, constant.Values, location, memberName, in reporter);
        }
    }
}
