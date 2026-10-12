using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Tangent.Core.Diagnostics;
using Tangent.Core.Ir;

namespace Tangent.Analyzers.Internal;

/// <summary>
/// The per-operation-block worker. One instance is created for each analysed member body, which keeps the
/// registered operation callbacks free of closures and lets the applicable rules be filtered exactly once
/// per body rather than once per operation.
/// </summary>
internal sealed class OperationAnalyzer
{
    private readonly CompilationState _state;
    private readonly TypeContext _context;
    private readonly ISymbol _owningSymbol;
    private readonly string _memberName;
    private readonly string _scope;
    private readonly ImmutableArray<AppliedRule> _usingRules;
    private readonly ImmutableArray<AppliedRule> _newRules;
    private readonly ImmutableArray<AppliedRule> _flowRules;

    private OperationAnalyzer(
        CompilationState state,
        TypeContext context,
        ISymbol owningSymbol,
        string memberName,
        ImmutableArray<AppliedRule> usingRules,
        ImmutableArray<AppliedRule> newRules,
        ImmutableArray<AppliedRule> flowRules)
    {
        _state = state;
        _context = context;
        _owningSymbol = owningSymbol;
        _memberName = memberName;
        _scope = "body:" + owningSymbol.Name;
        _usingRules = usingRules;
        _newRules = newRules;
        _flowRules = flowRules;
    }

    /// <summary>True when this body needs any operation callbacks at all.</summary>
    internal bool NeedsOperations =>
        !_usingRules.IsEmpty || !_newRules.IsEmpty || _context.LayerIndex >= 0 || _state.Sink is not null;

    /// <summary>True when this body is a constructor with flow rules to check.</summary>
    internal bool NeedsBlockEnd => !_flowRules.IsEmpty;

    /// <summary>Builds the worker for one operation block, or null when nothing applies to it.</summary>
    internal static OperationAnalyzer? Create(CompilationState state, TypeContext context, ISymbol owningSymbol)
    {
        MemberKind? kind = MemberKindOf(owningSymbol);
        ISymbol member = owningSymbol is IMethodSymbol { AssociatedSymbol: { } associated } ? associated : owningSymbol;

        // Both reference prohibitions ride the same scan: `forbid using Ns.**` and `forbid using contract X`.
        ImmutableArray<AppliedRule> usingRules = Filter(state, context, member, kind, context.ReferenceRules, PredicateKind.UsingNamespace)
            .AddRange(Filter(state, context, member, kind, context.ReferenceRules, PredicateKind.UsingContract));
        ImmutableArray<AppliedRule> newRules = Filter(state, context, member, kind, context.ReferenceRules, PredicateKind.NewExpression);

        ImmutableArray<AppliedRule> flowRules = ImmutableArray<AppliedRule>.Empty;
        if (owningSymbol is IMethodSymbol { MethodKind: MethodKind.Constructor, IsStatic: false })
        {
            flowRules = Filter(state, context, member, MemberKind.Ctor, context.FlowRules, null);
        }

        var analyzer = new OperationAnalyzer(state, context, owningSymbol, member.Name, usingRules, newRules, flowRules);
        return analyzer.NeedsOperations || analyzer.NeedsBlockEnd ? analyzer : null;
    }

    private static MemberKind? MemberKindOf(ISymbol symbol)
    {
        if (symbol is IMethodSymbol { AssociatedSymbol: { } associated }) return SymbolFacts.ClassifyMember(associated);
        return SymbolFacts.ClassifyMember(symbol);
    }

    private static ImmutableArray<AppliedRule> Filter(
        CompilationState state,
        TypeContext context,
        ISymbol member,
        MemberKind? kind,
        ImmutableArray<AppliedRule> source,
        PredicateKind? predicate)
    {
        ImmutableArray<AppliedRule>.Builder? builder = null;
        for (int i = 0; i < source.Length; i++)
        {
            Rule rule = state.Model.Contracts[source[i].ContractIndex].Rules[source[i].RuleIndex];
            if (predicate is not null && rule.Predicate.Kind != predicate.Value) continue;
            if (predicate is null && rule.Predicate.Kind == PredicateKind.Stateless) continue;

            if (rule.Selector.Kind == MemberKind.Type)
            {
                if (!state.EvaluateCondition(rule.Selector.ConditionIndex, context.Type, context.Type)) continue;
            }
            else
            {
                if (kind is null || rule.Selector.Kind != kind.Value) continue;
                if (!state.EvaluateCondition(rule.Selector.ConditionIndex, member, context.Type)) continue;
            }

            builder ??= ImmutableArray.CreateBuilder<AppliedRule>();
            builder.Add(source[i]);
        }

        return builder is null ? ImmutableArray<AppliedRule>.Empty : builder.ToImmutable();
    }

    /// <summary>The Tier 1 operation callback.</summary>
    internal void AnalyzeOperation(OperationAnalysisContext context)
    {
        var reporter = new DiagnosticReporter(context);
        IOperation operation = context.Operation;
        Location location = operation.Syntax.GetLocation();

        switch (operation)
        {
            case IInvocationOperation invocation:
            {
                IMethodSymbol target = invocation.TargetMethod;
                Observe(target.ContainingType, target, ReferenceKind.Invocation, location, in reporter);
                ObserveTypeArguments(target, ReferenceKind.Invocation, location, in reporter);
                break;
            }

            case IObjectCreationOperation creation:
            {
                ITypeSymbol? created = creation.Type;
                CheckForbiddenNew(created, location, in reporter);

                // The created type's own type arguments are walked by ReferenceScanner.
                Observe(created, null, ReferenceKind.ObjectCreation, location, in reporter);
                break;
            }

            case IFieldReferenceOperation field:
                Observe(field.Field.ContainingType, field.Field, ReferenceKind.MemberAccess, location, in reporter);
                break;

            case IPropertyReferenceOperation property:
                Observe(property.Property.ContainingType, property.Property, ReferenceKind.MemberAccess, location, in reporter);
                break;

            case IEventReferenceOperation @event:
                Observe(@event.Event.ContainingType, @event.Event, ReferenceKind.MemberAccess, location, in reporter);
                break;

            case IMethodReferenceOperation method:
                Observe(method.Method.ContainingType, method.Method, ReferenceKind.MemberAccess, location, in reporter);
                ObserveTypeArguments(method.Method, ReferenceKind.MemberAccess, location, in reporter);
                break;

            case ITypeOfOperation typeOf:
                Observe(typeOf.TypeOperand, null, ReferenceKind.TypeOf, location, in reporter);
                break;

            // `x is ForbiddenType`.
            case IIsTypeOperation isType:
                Observe(isType.TypeOperand, null, ReferenceKind.Other, location, in reporter);
                break;

            // `x is ForbiddenType t`, `case ForbiddenType:`, `x is ForbiddenType { ... }`.
            case IDeclarationPatternOperation declarationPattern:
                Observe(declarationPattern.MatchedType, null, ReferenceKind.Other, location, in reporter);
                break;

            case ITypePatternOperation typePattern:
                Observe(typePattern.MatchedType, null, ReferenceKind.Other, location, in reporter);
                break;

            case IRecursivePatternOperation recursivePattern:
            {
                // `x is { Length: 0 }` names no type; only a narrowing pattern is a written reference.
                ITypeSymbol matched = recursivePattern.MatchedType;
                if (!SymbolEqualityComparer.Default.Equals(matched, recursivePattern.InputType))
                {
                    Observe(matched, null, ReferenceKind.Other, location, in reporter);
                }

                break;
            }

            // `(ForbiddenType)x` and `x as ForbiddenType`. Conversions the compiler inserts name nothing.
            case IConversionOperation conversion:
                if (conversion.IsTryCast || !conversion.IsImplicit || !conversion.Conversion.IsImplicit)
                {
                    Observe(conversion.Type, null, ReferenceKind.Other, location, in reporter);
                }

                break;

            // `ForbiddenType x = ...;` and `foreach (ForbiddenType x in ...)`.
            case IVariableDeclaratorOperation declarator:
            {
                TypeSyntax? declaredType = DeclaredTypeSyntax(declarator);

                // `var` writes no type name: the initializer already carries the reference, and reporting
                // both would double up on the same pair.
                if (declaredType is not null && !declaredType.IsVar)
                {
                    Observe(declarator.Symbol.Type, null, ReferenceKind.Other, declaredType.GetLocation(), in reporter);
                }

                break;
            }

            // `new ForbiddenType[4]` — the element type is unwrapped by ReferenceScanner.
            case IArrayCreationOperation arrayCreation:
                Observe(arrayCreation.Type, null, ReferenceKind.ObjectCreation, location, in reporter);
                break;

            case IDefaultValueOperation defaultValue:
                Observe(defaultValue.Type, null, ReferenceKind.Other, location, in reporter);
                break;

            case ISizeOfOperation sizeOf:
                Observe(sizeOf.TypeOperand, null, ReferenceKind.Other, location, in reporter);
                break;

            default:
                break;
        }
    }

    /// <summary>Observes the type arguments a generic method was invoked or referenced with.</summary>
    private void ObserveTypeArguments(IMethodSymbol method, ReferenceKind kind, Location location, in DiagnosticReporter reporter)
    {
        ImmutableArray<ITypeSymbol> arguments = method.TypeArguments;
        for (int i = 0; i < arguments.Length; i++)
        {
            Observe(arguments[i], null, kind, location, in reporter);
        }
    }

    /// <summary>The type syntax a local declaration wrote, or null when the shape is not a plain declaration.</summary>
    private static TypeSyntax? DeclaredTypeSyntax(IVariableDeclaratorOperation declarator)
    {
        switch (declarator.Syntax)
        {
            case VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration }:
                return declaration.Type;
            case ForEachStatementSyntax forEach:
                return forEach.Type;
            default:
                return null;
        }
    }

    /// <summary>The Tier 2 callback, run once the whole constructor body is available.</summary>
    internal void AnalyzeBlockEnd(OperationBlockAnalysisContext context)
    {
        if (_flowRules.IsEmpty) return;
        if (_owningSymbol is not IMethodSymbol constructor) return;
        var reporter = new DiagnosticReporter(context);
        FlowAnalysis.AnalyzeConstructor(_state, _context, constructor, context.OperationBlocks, _flowRules, in reporter);
    }

    private void Observe(ITypeSymbol? type, ISymbol? symbol, ReferenceKind kind, Location location, in DiagnosticReporter reporter) =>
        ReferenceScanner.Observe(_state, _context, _usingRules, type, symbol, kind, location, _memberName, _scope, in reporter);

    private void CheckForbiddenNew(ITypeSymbol? created, Location location, in DiagnosticReporter reporter)
    {
        if (created is null || _newRules.IsEmpty) return;

        ArchitectureModel model = _state.Model;
        for (int i = 0; i < _newRules.Length; i++)
        {
            Rule rule = model.Contracts[_newRules[i].ContractIndex].Rules[_newRules[i].RuleIndex];

            // `new X` is only ever meaningful as a prohibition: the rule fires when the created type, or
            // any of its base types, matches the pattern.
            if (rule.Polarity != Polarity.Forbid) continue;
            if (!PatternMatching.MatchesGroupIncludingBases(
                    model, rule.Predicate.Payload, created, _context.Type.Name.AsSpan(), _context.NamespaceName.AsSpan()))
            {
                continue;
            }

            string contract = model.Text(model.Contracts[_newRules[i].ContractIndex].Name);
            reporter.Report(MessageFactory.Create(
                DiagnosticCatalog.ForbiddenNewViolation.Id,
                location,
                MessageFactory.Violation(
                    DiagnosticCatalog.ForbiddenNewViolation, model, in rule, string.Empty,
                    contract, created.Name),
                MessageFactory.Properties(contract, rule.Id.Value, _memberName, rule.Tier)));
        }
    }
}
