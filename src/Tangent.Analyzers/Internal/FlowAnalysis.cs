using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Tangent.Core.Diagnostics;
using Tangent.Core.Ir;

namespace Tangent.Analyzers.Internal;

/// <summary>
/// Tier 2, the curated flow rules: <c>each parameter assigned to [modifiers] field</c>,
/// <c>each parameter null-checked</c> and <c>each parameter not stored</c>. <c>stateless</c> is derivable
/// from symbols alone and is evaluated in the symbol action instead.
/// </summary>
internal static class FlowAnalysis
{
    /// <summary>Runs the flow rules for one constructor, given its operation blocks.</summary>
    internal static void AnalyzeConstructor(
        CompilationState state,
        TypeContext context,
        IMethodSymbol constructor,
        ImmutableArray<IOperation> blocks,
        ImmutableArray<AppliedRule> rules,
        in DiagnosticReporter reporter)
    {
        if (constructor.Parameters.Length == 0) return;

        var assignedToMatchingField = new HashSet<string>(StringComparer.Ordinal);
        var storedAnywhere = new HashSet<string>(StringComparer.Ordinal);
        var nullChecked = new HashSet<string>(StringComparer.Ordinal);
        Modifiers fieldMask = Modifiers.None;

        for (int i = 0; i < rules.Length; i++)
        {
            Rule rule = state.Model.Contracts[rules[i].ContractIndex].Rules[rules[i].RuleIndex];
            if (rule.Predicate.Kind == PredicateKind.EachParamAssignedToField) fieldMask = (Modifiers)rule.Predicate.Payload;
        }

        for (int b = 0; b < blocks.Length; b++)
        {
            Collect(blocks[b], fieldMask, assignedToMatchingField, storedAnywhere, nullChecked);
        }

        Report(state, context, constructor, rules, assignedToMatchingField, storedAnywhere, nullChecked, in reporter);
    }

    /// <summary>
    /// Runs <c>each parameter assigned to field</c> for a primary constructor. Primary constructors have no
    /// body to walk, so a parameter counts only when an explicit field is initialised from it.
    /// </summary>
    internal static void AnalyzePrimaryConstructor(
        CompilationState state,
        TypeContext context,
        IMethodSymbol constructor,
        ImmutableArray<AppliedRule> rules,
        in DiagnosticReporter reporter)
    {
        if (constructor.Parameters.Length == 0) return;

        var assignedToMatchingField = new HashSet<string>(StringComparer.Ordinal);
        var storedAnywhere = new HashSet<string>(StringComparer.Ordinal);
        var nullChecked = new HashSet<string>(StringComparer.Ordinal);
        Modifiers fieldMask = Modifiers.None;

        for (int i = 0; i < rules.Length; i++)
        {
            Rule rule = state.Model.Contracts[rules[i].ContractIndex].Rules[rules[i].RuleIndex];
            if (rule.Predicate.Kind == PredicateKind.EachParamAssignedToField) fieldMask = (Modifiers)rule.Predicate.Payload;
        }

        ImmutableArray<ISymbol> members = context.Type.GetMembers();
        for (int i = 0; i < members.Length; i++)
        {
            if (members[i] is not IFieldSymbol field || field.IsStatic) continue;
            string? captured = CapturedParameterName(field);
            if (captured is null) continue;
            storedAnywhere.Add(captured);
            if (SymbolFacts.HasModifiers(field, fieldMask)) assignedToMatchingField.Add(captured);
        }

        Report(state, context, constructor, rules, assignedToMatchingField, storedAnywhere, nullChecked, in reporter);
    }

    private static string? CapturedParameterName(IFieldSymbol field)
    {
        ImmutableArray<SyntaxReference> references = field.DeclaringSyntaxReferences;
        for (int i = 0; i < references.Length; i++)
        {
            if (references[i].GetSyntax() is VariableDeclaratorSyntax declarator &&
                declarator.Initializer?.Value is IdentifierNameSyntax identifier)
            {
                return identifier.Identifier.ValueText;
            }
        }

        return null;
    }

    private static void Report(
        CompilationState state,
        TypeContext context,
        IMethodSymbol constructor,
        ImmutableArray<AppliedRule> rules,
        HashSet<string> assignedToMatchingField,
        HashSet<string> storedAnywhere,
        HashSet<string> nullChecked,
        in DiagnosticReporter reporter)
    {
        ArchitectureModel model = state.Model;
        ImmutableArray<IParameterSymbol> parameters = constructor.Parameters;

        for (int i = 0; i < rules.Length; i++)
        {
            Rule rule = model.Contracts[rules[i].ContractIndex].Rules[rules[i].RuleIndex];
            string contract = model.Text(model.Contracts[rules[i].ContractIndex].Name);

            switch (rule.Predicate.Kind)
            {
                case PredicateKind.EachParamAssignedToField:
                {
                    if (rule.Polarity != Polarity.Require) break;
                    for (int p = 0; p < parameters.Length; p++)
                    {
                        if (assignedToMatchingField.Contains(parameters[p].Name)) continue;
                        reporter.Report(MessageFactory.Create(
                            DiagnosticCatalog.ParameterNotAssignedToField.Id,
                            SymbolFacts.LocationOf(parameters[p]),
                            MessageFactory.Violation(
                                DiagnosticCatalog.ParameterNotAssignedToField, model, in rule, string.Empty,
                                contract, "'" + parameters[p].Name + "'"),
                            MessageFactory.Properties(contract, rule.Id.Value, parameters[p].Name, rule.Tier)));
                    }

                    break;
                }

                case PredicateKind.EachParamNullChecked:
                {
                    if (rule.Polarity != Polarity.Require) break;
                    for (int p = 0; p < parameters.Length; p++)
                    {
                        ITypeSymbol type = parameters[p].Type;
                        if (type.IsValueType) continue;
                        if (nullChecked.Contains(parameters[p].Name)) continue;
                        reporter.Report(MessageFactory.Create(
                            DiagnosticCatalog.ParameterNotNullChecked.Id,
                            SymbolFacts.LocationOf(parameters[p]),
                            MessageFactory.Violation(
                                DiagnosticCatalog.ParameterNotNullChecked, model, in rule, string.Empty,
                                contract, "'" + parameters[p].Name + "'"),
                            MessageFactory.Properties(contract, rule.Id.Value, parameters[p].Name, rule.Tier)));
                    }

                    break;
                }

                case PredicateKind.EachParamNotStored:
                {
                    for (int p = 0; p < parameters.Length; p++)
                    {
                        if (!storedAnywhere.Contains(parameters[p].Name)) continue;
                        reporter.Report(MessageFactory.Create(
                            DiagnosticCatalog.ParameterNotAssignedToField.Id,
                            SymbolFacts.LocationOf(parameters[p]),
                            contract + ": constructor parameter '" + parameters[p].Name +
                            "' must not be stored in a field or property" +
                            ". (rule " + rule.Id.Value + ", " + MessageFactory.Origin(model, rule.Span) + ")",
                            MessageFactory.Properties(contract, rule.Id.Value, parameters[p].Name, rule.Tier)));
                    }

                    break;
                }
            }
        }
    }

    // ------------------------------------------------------------------
    // operation walking
    // ------------------------------------------------------------------

    private static void Collect(
        IOperation root,
        Modifiers fieldMask,
        HashSet<string> assignedToMatchingField,
        HashSet<string> storedAnywhere,
        HashSet<string> nullChecked)
    {
        Visit(root, fieldMask, assignedToMatchingField, storedAnywhere, nullChecked);
        foreach (IOperation operation in root.Descendants())
        {
            Visit(operation, fieldMask, assignedToMatchingField, storedAnywhere, nullChecked);
        }
    }

    private static void Visit(
        IOperation operation,
        Modifiers fieldMask,
        HashSet<string> assignedToMatchingField,
        HashSet<string> storedAnywhere,
        HashSet<string> nullChecked)
    {
        switch (operation)
        {
            case ISimpleAssignmentOperation assignment:
            {
                if (ParameterOf(assignment.Value) is not IParameterSymbol parameter) break;
                if (assignment.Target is IFieldReferenceOperation fieldReference)
                {
                    storedAnywhere.Add(parameter.Name);
                    if (SymbolFacts.HasModifiers(fieldReference.Field, fieldMask)) assignedToMatchingField.Add(parameter.Name);
                }
                else if (assignment.Target is IPropertyReferenceOperation)
                {
                    storedAnywhere.Add(parameter.Name);
                }

                break;
            }

            case IInvocationOperation invocation:
            {
                if (!IsThrowIfNull(invocation.TargetMethod)) break;
                ImmutableArray<IArgumentOperation> arguments = invocation.Arguments;
                if (arguments.Length == 0) break;
                if (ParameterOf(arguments[0].Value) is IParameterSymbol checkedParameter) nullChecked.Add(checkedParameter.Name);
                break;
            }

            case ICoalesceOperation coalesce:
            {
                if (coalesce.WhenNull is not IThrowOperation && coalesce.WhenNull is not IConversionOperation { Operand: IThrowOperation }) break;
                if (ParameterOf(coalesce.Value) is IParameterSymbol coalesced) nullChecked.Add(coalesced.Name);
                break;
            }

            case IConditionalOperation conditional:
            {
                if (!ContainsThrow(conditional.WhenTrue) && !ContainsThrow(conditional.WhenFalse)) break;
                MarkParameters(conditional.Condition, nullChecked);
                break;
            }
        }
    }

    private static bool IsThrowIfNull(IMethodSymbol method) =>
        string.Equals(method.Name, "ThrowIfNull", StringComparison.Ordinal) ||
        string.Equals(method.Name, "ThrowIfNullOrEmpty", StringComparison.Ordinal) ||
        string.Equals(method.Name, "ThrowIfNullOrWhiteSpace", StringComparison.Ordinal);

    private static void MarkParameters(IOperation? condition, HashSet<string> nullChecked)
    {
        if (condition is null) return;
        if (ParameterOf(condition) is IParameterSymbol direct) nullChecked.Add(direct.Name);
        foreach (IOperation operation in condition.Descendants())
        {
            if (operation is IParameterReferenceOperation parameter) nullChecked.Add(parameter.Parameter.Name);
        }
    }

    private static bool ContainsThrow(IOperation? operation)
    {
        if (operation is null) return false;
        if (operation is IThrowOperation) return true;
        foreach (IOperation child in operation.Descendants())
        {
            if (child is IThrowOperation) return true;
        }

        return false;
    }

    private static IParameterSymbol? ParameterOf(IOperation? operation)
    {
        while (operation is IConversionOperation conversion) operation = conversion.Operand;
        if (operation is ICoalesceOperation coalesce) operation = coalesce.Value;
        while (operation is IConversionOperation inner) operation = inner.Operand;
        return operation is IParameterReferenceOperation reference ? reference.Parameter : null;
    }
}
