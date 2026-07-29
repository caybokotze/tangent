using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Tangent.Core.Ir;
using Tangent.Core.Matching;

namespace Tangent.Analyzers.Internal;

/// <summary>
/// Structural matching of Core's compiled patterns against Roslyn symbols. Everything here walks the IR
/// side tables and the symbol graph directly; no display strings are materialised.
/// </summary>
internal static class PatternMatching
{
    private const int TemplateBufferLength = 256;

    /// <summary>True when any pattern in the group matches the type.</summary>
    internal static bool MatchesGroup(
        ArchitectureModel model,
        int groupIndex,
        ITypeSymbol? type,
        ReadOnlySpan<char> boundName,
        ReadOnlySpan<char> boundNamespace)
    {
        if (type is null) return false;
        ImmutableArray<int> group = model.TypePatterns.Group(groupIndex);
        for (int i = 0; i < group.Length; i++)
        {
            if (Matches(model, group[i], type, boundName, boundNamespace)) return true;
        }

        return false;
    }

    /// <summary>True when the type, or any of its base types, matches the group.</summary>
    internal static bool MatchesGroupIncludingBases(
        ArchitectureModel model,
        int groupIndex,
        ITypeSymbol? type,
        ReadOnlySpan<char> boundName,
        ReadOnlySpan<char> boundNamespace)
    {
        ITypeSymbol? current = type;
        while (current is not null)
        {
            if (MatchesGroup(model, groupIndex, current, boundName, boundNamespace)) return true;
            current = current.BaseType;
        }

        return false;
    }

    /// <summary>True when any base type of <paramref name="type"/> matches the group.</summary>
    internal static bool AnyBaseMatches(
        ArchitectureModel model,
        int groupIndex,
        INamedTypeSymbol type,
        ReadOnlySpan<char> boundName,
        ReadOnlySpan<char> boundNamespace)
    {
        INamedTypeSymbol? current = type.BaseType;
        while (current is not null)
        {
            if (MatchesGroup(model, groupIndex, current, boundName, boundNamespace)) return true;
            current = current.BaseType;
        }

        return false;
    }

    /// <summary>True when any interface of <paramref name="type"/> matches the group.</summary>
    internal static bool AnyInterfaceMatches(
        ArchitectureModel model,
        int groupIndex,
        INamedTypeSymbol type,
        ReadOnlySpan<char> boundName,
        ReadOnlySpan<char> boundNamespace)
    {
        ImmutableArray<INamedTypeSymbol> interfaces = type.AllInterfaces;
        for (int i = 0; i < interfaces.Length; i++)
        {
            if (MatchesGroup(model, groupIndex, interfaces[i], boundName, boundNamespace)) return true;
        }

        return false;
    }

    /// <summary>True when one compiled pattern matches one type symbol.</summary>
    internal static bool Matches(
        ArchitectureModel model,
        int patternIndex,
        ITypeSymbol? type,
        ReadOnlySpan<char> boundName,
        ReadOnlySpan<char> boundNamespace)
    {
        if (type is null) return false;

        TypePattern pattern = model.TypePatterns.Patterns[patternIndex];
        if (pattern.IsWildcard) return true;

        if (type is IArrayTypeSymbol array)
        {
            // `T[]` only matches a wildcard or an element-type pattern spelled without the brackets.
            return Matches(model, patternIndex, array.ElementType, boundName, boundNamespace);
        }

        if (pattern.TemplateIndex >= 0)
        {
            return MatchesTemplated(model, patternIndex, in pattern, type, boundName, boundNamespace);
        }

        return MatchesNamed(model, in pattern, type, model.Strings.GetSpan(pattern.Name), boundName, boundNamespace);
    }

    private static bool MatchesTemplated(
        ArchitectureModel model,
        int patternIndex,
        in TypePattern pattern,
        ITypeSymbol type,
        ReadOnlySpan<char> boundName,
        ReadOnlySpan<char> boundNamespace)
    {
        Span<char> buffer = stackalloc char[TemplateBufferLength];
        Template template = model.TypePatterns.Templates[pattern.TemplateIndex];
        if (!template.Expand(boundName, boundNamespace, buffer, out int written)) return false;
        return MatchesNamed(model, in pattern, type, buffer.Slice(0, written), boundName, boundNamespace);
    }

    private static bool MatchesNamed(
        ArchitectureModel model,
        in TypePattern pattern,
        ITypeSymbol type,
        ReadOnlySpan<char> expected,
        ReadOnlySpan<char> boundName,
        ReadOnlySpan<char> boundNamespace)
    {
        if (expected.Length == 0) return false;

        if (type.SpecialType == SpecialType.System_Void)
        {
            return pattern.Arity == 0 && IsVoidKeyword(expected);
        }

        if (!QualifiedNameMatches(type, expected)) return false;

        var named = type as INamedTypeSymbol;
        int arity = named?.Arity ?? 0;
        if (arity != pattern.Arity) return false;
        if (arity == 0) return true;

        ImmutableArray<ITypeSymbol> arguments = named!.TypeArguments;
        TypePatternTable table = model.TypePatterns;
        for (int i = 0; i < pattern.Arity; i++)
        {
            if (!Matches(model, table.Argument(pattern, i), arguments[i], boundName, boundNamespace)) return false;
        }

        return true;
    }

    private static bool IsVoidKeyword(ReadOnlySpan<char> expected) =>
        expected.Length == 4 &&
        (expected[0] == 'v' || expected[0] == 'V') &&
        expected[1] == 'o' && expected[2] == 'i' && expected[3] == 'd';

    /// <summary>
    /// A pattern name with no dot matches the simple name in any namespace; a dotted pattern must match
    /// the namespace-qualified name as a suffix at a segment boundary.
    /// </summary>
    internal static bool QualifiedNameMatches(ITypeSymbol type, ReadOnlySpan<char> expected)
    {
        int dot = expected.LastIndexOf('.');
        ReadOnlySpan<char> segment = dot < 0 ? expected : expected.Slice(dot + 1);
        if (!segment.SequenceEqual(type.Name.AsSpan())) return false;
        if (dot < 0) return true;

        expected = expected.Slice(0, dot);

        INamedTypeSymbol? containingType = type.ContainingType;
        while (expected.Length > 0 && containingType is not null)
        {
            dot = expected.LastIndexOf('.');
            segment = dot < 0 ? expected : expected.Slice(dot + 1);
            if (!segment.SequenceEqual(containingType.Name.AsSpan())) return false;
            expected = dot < 0 ? ReadOnlySpan<char>.Empty : expected.Slice(0, dot);
            containingType = containingType.ContainingType;
        }

        INamespaceSymbol? ns = type.ContainingNamespace;
        while (expected.Length > 0 && ns is not null && !ns.IsGlobalNamespace)
        {
            dot = expected.LastIndexOf('.');
            segment = dot < 0 ? expected : expected.Slice(dot + 1);
            if (!segment.SequenceEqual(ns.Name.AsSpan())) return false;
            expected = dot < 0 ? ReadOnlySpan<char>.Empty : expected.Slice(0, dot);
            ns = ns.ContainingNamespace;
        }

        return expected.Length == 0;
    }

    /// <summary>True when the symbol carries an attribute satisfying the group.</summary>
    internal static bool HasAttribute(ArchitectureModel model, int groupIndex, ISymbol symbol)
    {
        AttributeGroup group = model.AttributeGroups[groupIndex];
        ImmutableArray<AttributeData> attributes = symbol.GetAttributes();
        for (int i = 0; i < attributes.Length; i++)
        {
            INamedTypeSymbol? attributeClass = attributes[i].AttributeClass;
            if (attributeClass is null) continue;
            if (group.Matches(attributeClass.Name.AsSpan())) return true;
        }

        return false;
    }

    /// <summary>True when the attribute group names <paramref name="name"/> (used by <c>bind ... except [A]</c>).</summary>
    internal static bool GroupNames(ArchitectureModel model, int groupIndex, ReadOnlySpan<char> name)
    {
        AttributeGroup group = model.AttributeGroups[groupIndex];
        ImmutableArray<AttributeAlternative> alternatives = group.Alternatives;
        for (int i = 0; i < alternatives.Length; i++)
        {
            if (AttributeGroup.NameEquals(name, model.Strings.GetSpan(alternatives[i].Name))) return true;
        }

        return false;
    }
}
