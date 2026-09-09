using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Tangent.Core.Ir;
using Tangent.Core.Matching;
using Tangent.LanguageServer.Roslyn;
using CoreTypeKind = Tangent.Core.Ir.TypeKind;

namespace Tangent.LanguageServer.Features;

/// <summary>The set of C# types a contract currently applies to, and how confident we are about it.</summary>
/// <param name="Types">Fully qualified type names, sorted.</param>
/// <param name="Truncated">True when more types matched than were returned.</param>
/// <param name="Approximate">True when a <c>where</c> condition was assumed to hold rather than evaluated.</param>
internal readonly record struct BoundTypeSet(ImmutableArray<string> Types, bool Truncated, bool Approximate);

/// <summary>
/// Evaluates <c>match</c> clauses and <c>bind</c> statements against the indexed types.
/// Only the cheap half of a match clause is evaluated — type kind, the namespace glob and the
/// <c>name</c> conditions. Structural conditions (<c>inherits</c>, <c>implements</c>,
/// <c>attributes</c>) need real symbol walking, so they are assumed to hold and the result is
/// flagged approximate; hover says so rather than pretending.
/// </summary>
internal static class ContractBinding
{
    /// <summary>Computes the types bound to a contract, capped at <paramref name="limit"/> entries.</summary>
    public static BoundTypeSet Evaluate(
        ArchitectureModel model,
        int contractIndex,
        CompilationContext compilation,
        int limit = 30)
    {
        if (contractIndex < 0 || contractIndex >= model.Contracts.Length)
        {
            return new BoundTypeSet(ImmutableArray<string>.Empty, false, false);
        }

        var names = new SortedSet<string>(StringComparer.Ordinal);
        bool approximate = false;

        foreach (Binding binding in model.Bindings)
        {
            if (binding.ContractIndices.Contains(contractIndex))
            {
                names.Add(model.Text(binding.TypeName));
            }
        }

        Contract contract = model.Contracts[contractIndex];
        if (contract.Match is MatchClause match)
        {
            Glob glob = model.Globs[match.GlobIndex];
            foreach (INamedTypeSymbol type in compilation.Types.SourceTypes)
            {
                if (!KindMatches(match.Kind, type)) continue;
                if (!glob.Matches(TypeIndex.NamespaceOf(type).AsSpan())) continue;
                if (!ConditionHolds(model, match.ConditionIndex, type.Name, ref approximate)) continue;
                names.Add(TypeIndex.QualifiedName(type));
            }
        }

        bool truncated = names.Count > limit;
        ImmutableArray<string> result = names.Take(limit).ToImmutableArray();
        return new BoundTypeSet(result, truncated, approximate);
    }

    private static bool KindMatches(CoreTypeKind kind, INamedTypeSymbol type) => kind switch
    {
        CoreTypeKind.Any => true,
        CoreTypeKind.Class => type.TypeKind == Microsoft.CodeAnalysis.TypeKind.Class && !type.IsRecord,
        CoreTypeKind.Record => type.IsRecord,
        CoreTypeKind.Struct => type.TypeKind == Microsoft.CodeAnalysis.TypeKind.Struct && !type.IsRecord,
        CoreTypeKind.Interface => type.TypeKind == Microsoft.CodeAnalysis.TypeKind.Interface,
        CoreTypeKind.Enum => type.TypeKind == Microsoft.CodeAnalysis.TypeKind.Enum,
        _ => true,
    };

    private static bool ConditionHolds(ArchitectureModel model, int index, string typeName, ref bool approximate)
    {
        if (index < 0 || index >= model.Conditions.Length) return true;
        Condition condition = model.Conditions[index];
        switch (condition.Kind)
        {
            case ConditionKind.And:
                return ConditionHolds(model, condition.Left, typeName, ref approximate)
                    && ConditionHolds(model, condition.Right, typeName, ref approximate);

            case ConditionKind.Or:
                return ConditionHolds(model, condition.Left, typeName, ref approximate)
                    || ConditionHolds(model, condition.Right, typeName, ref approximate);

            case ConditionKind.NameStartsWith:
                return typeName.StartsWith(model.Text(new Core.Text.StringId(condition.Payload)), StringComparison.Ordinal);

            case ConditionKind.NameEndsWith:
                return typeName.EndsWith(model.Text(new Core.Text.StringId(condition.Payload)), StringComparison.Ordinal);

            case ConditionKind.NameContains:
                return typeName.Contains(model.Text(new Core.Text.StringId(condition.Payload)), StringComparison.Ordinal);

            default:
                // NameMatches (regex), Inherits, Implements, HasAttributes, Modifier.
                approximate = true;
                return true;
        }
    }
}
