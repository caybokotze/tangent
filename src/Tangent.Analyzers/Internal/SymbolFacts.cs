using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Tangent.Core.Ir;
using CoreTypeKind = Tangent.Core.Ir.TypeKind;
using RoslynTypeKind = Microsoft.CodeAnalysis.TypeKind;

namespace Tangent.Analyzers.Internal;

/// <summary>
/// Symbol-level facts the evaluator needs: modifier masks, member-kind classification and recognition of
/// the Tangent attributes. Everything here works from <see cref="ISymbol"/> alone and allocates nothing.
/// </summary>
internal static class SymbolFacts
{
    internal const string TangentNamespace = "Tangent";
    internal const string TangentAttributeName = "TangentAttribute";
    internal const string TangentIgnoreAttributeName = "TangentIgnoreAttribute";

    /// <summary>True when the attribute is <c>Tangent.TangentAttribute</c>, matched by metadata name only.</summary>
    internal static bool IsTangentAttribute(INamedTypeSymbol? attributeClass) =>
        IsTangentAttribute(attributeClass, TangentAttributeName);

    /// <summary>True when the attribute is <c>Tangent.TangentIgnoreAttribute</c>.</summary>
    internal static bool IsTangentIgnoreAttribute(INamedTypeSymbol? attributeClass) =>
        IsTangentAttribute(attributeClass, TangentIgnoreAttributeName);

    private static bool IsTangentAttribute(INamedTypeSymbol? attributeClass, string name)
    {
        if (attributeClass is null) return false;
        if (!string.Equals(attributeClass.Name, name, StringComparison.Ordinal)) return false;
        INamespaceSymbol? ns = attributeClass.ContainingNamespace;
        if (ns is null || !string.Equals(ns.Name, TangentNamespace, StringComparison.Ordinal)) return false;
        return ns.ContainingNamespace is null || ns.ContainingNamespace.IsGlobalNamespace;
    }

    /// <summary>True when every modifier in <paramref name="mask"/> holds for <paramref name="symbol"/>.</summary>
    internal static bool HasModifiers(ISymbol symbol, Modifiers mask)
    {
        if (mask == Modifiers.None) return true;

        Modifiers accessibility = mask & Modifiers.AccessibilityMask;
        if (accessibility != Modifiers.None && (AccessibilityOf(symbol) & accessibility) == Modifiers.None) return false;

        if ((mask & Modifiers.Static) != 0 && !symbol.IsStatic) return false;
        if ((mask & Modifiers.Sealed) != 0 && !IsSealed(symbol)) return false;
        if ((mask & Modifiers.Abstract) != 0 && !symbol.IsAbstract) return false;
        if ((mask & Modifiers.Readonly) != 0 && !IsReadOnly(symbol)) return false;
        if ((mask & Modifiers.Partial) != 0 && !IsPartial(symbol)) return false;

        Modifiers typeKind = mask & Modifiers.TypeKindMask;
        if (typeKind != Modifiers.None && (TypeKindOf(symbol) & typeKind) == Modifiers.None) return false;

        return true;
    }

    private static Modifiers AccessibilityOf(ISymbol symbol)
    {
        switch (symbol.DeclaredAccessibility)
        {
            case Accessibility.Public: return Modifiers.Public;
            case Accessibility.Private: return Modifiers.Private;
            case Accessibility.Protected: return Modifiers.Protected;
            case Accessibility.Internal: return Modifiers.Internal;
            case Accessibility.ProtectedOrInternal:
            case Accessibility.ProtectedAndInternal:
                return Modifiers.Protected | Modifiers.Internal;
            default: return Modifiers.None;
        }
    }

    private static bool IsSealed(ISymbol symbol)
    {
        if (symbol is INamedTypeSymbol type)
        {
            // Structs and enums are sealed by construction; a `sealed` rule is about classes and records.
            return type.IsSealed;
        }

        return symbol.IsSealed;
    }

    private static bool IsReadOnly(ISymbol symbol)
    {
        switch (symbol)
        {
            case IFieldSymbol field: return field.IsReadOnly;
            case IPropertySymbol property: return property.IsReadOnly;
            case INamedTypeSymbol type: return type.IsReadOnly;
            case IMethodSymbol method: return method.IsReadOnly;
            default: return false;
        }
    }

    private static bool IsPartial(ISymbol symbol)
    {
        ImmutableArray<SyntaxReference> references = symbol.DeclaringSyntaxReferences;
        for (int i = 0; i < references.Length; i++)
        {
            if (references[i].GetSyntax() is MemberDeclarationSyntax member &&
                member.Modifiers.IndexOf(SyntaxKind.PartialKeyword) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static Modifiers TypeKindOf(ISymbol symbol)
    {
        if (symbol is not INamedTypeSymbol type) return Modifiers.None;
        switch (type.TypeKind)
        {
            case RoslynTypeKind.Class: return type.IsRecord ? Modifiers.Record | Modifiers.Class : Modifiers.Class;
            case RoslynTypeKind.Struct: return type.IsRecord ? Modifiers.Record | Modifiers.Struct : Modifiers.Struct;
            case RoslynTypeKind.Interface: return Modifiers.Interface;
            default: return Modifiers.None;
        }
    }

    /// <summary>True when the type symbol matches a <c>match</c> clause's type kind.</summary>
    internal static bool MatchesTypeKind(INamedTypeSymbol type, CoreTypeKind kind)
    {
        switch (kind)
        {
            case CoreTypeKind.Any: return true;
            case CoreTypeKind.Class: return type.TypeKind == RoslynTypeKind.Class;
            case CoreTypeKind.Interface: return type.TypeKind == RoslynTypeKind.Interface;
            case CoreTypeKind.Record: return type.IsRecord;
            case CoreTypeKind.Struct: return type.TypeKind == RoslynTypeKind.Struct;
            case CoreTypeKind.Enum: return type.TypeKind == RoslynTypeKind.Enum;
            default: return false;
        }
    }

    /// <summary>
    /// Classifies a member for the selector it belongs to, or null when the member is not something a
    /// Tangent selector ever addresses (accessors, operators, compiler-synthesised members, backing fields).
    /// </summary>
    internal static MemberKind? ClassifyMember(ISymbol member)
    {
        if (member.IsImplicitlyDeclared) return null;
        if (member.Name.Length > 0 && member.Name[0] == '<') return null;

        switch (member)
        {
            case IMethodSymbol method:
                if (method.MethodKind == MethodKind.Constructor) return method.IsStatic ? (MemberKind?)null : MemberKind.Ctor;
                if (method.MethodKind != MethodKind.Ordinary) return null;
                if (method.AssociatedSymbol is not null) return null;
                if (IsSynthesisedRecordMethod(method)) return null;
                return MemberKind.Method;

            case IPropertySymbol property:
                if (property.IsIndexer) return MemberKind.Property;
                if (IsEqualityContract(property)) return null;
                return MemberKind.Property;

            case IFieldSymbol field:
                if (field.AssociatedSymbol is not null) return null;
                return MemberKind.Field;

            case IEventSymbol: return MemberKind.Event;
            case INamedTypeSymbol: return MemberKind.Nested;
            default: return null;
        }
    }

    private static bool IsEqualityContract(IPropertySymbol property) =>
        property.ContainingType is { IsRecord: true } &&
        string.Equals(property.Name, "EqualityContract", StringComparison.Ordinal);

    private static bool IsSynthesisedRecordMethod(IMethodSymbol method)
    {
        if (method.ContainingType is not { IsRecord: true }) return false;
        switch (method.Name)
        {
            case "Equals":
            case "GetHashCode":
            case "ToString":
            case "PrintMembers":
            case "Deconstruct":
            case "Clone":
                // Only synthesised copies have no method declaration of their own.
                return !HasMethodDeclaration(method);
            default:
                return false;
        }
    }

    private static bool HasMethodDeclaration(IMethodSymbol method)
    {
        ImmutableArray<SyntaxReference> references = method.DeclaringSyntaxReferences;
        for (int i = 0; i < references.Length; i++)
        {
            if (references[i].GetSyntax() is MethodDeclarationSyntax) return true;
        }

        return false;
    }

    /// <summary>True when the constructor is a primary constructor (declared by the type header itself).</summary>
    internal static bool IsPrimaryConstructor(IMethodSymbol constructor)
    {
        ImmutableArray<SyntaxReference> references = constructor.DeclaringSyntaxReferences;
        for (int i = 0; i < references.Length; i++)
        {
            if (references[i].GetSyntax() is TypeDeclarationSyntax) return true;
        }

        return false;
    }

    /// <summary>The best location for a symbol: its identifier in source, or <see cref="Location.None"/>.</summary>
    internal static Location LocationOf(ISymbol symbol)
    {
        ImmutableArray<Location> locations = symbol.Locations;
        for (int i = 0; i < locations.Length; i++)
        {
            if (locations[i].IsInSource) return locations[i];
        }

        return Location.None;
    }

    /// <summary>The location of one of a symbol's attributes, or the symbol's own location.</summary>
    internal static Location LocationOf(ISymbol symbol, AttributeData attribute)
    {
        SyntaxReference? reference = attribute.ApplicationSyntaxReference;
        if (reference is not null) return Location.Create(reference.SyntaxTree, reference.Span);
        return LocationOf(symbol);
    }

    /// <summary>A human description of a member for diagnostic messages, such as <c>method 'GetById'</c>.</summary>
    internal static string DescribeMember(ISymbol member, MemberKind kind)
    {
        switch (kind)
        {
            case MemberKind.Ctor: return "constructor";
            case MemberKind.Method: return "method '" + member.Name + "'";
            case MemberKind.Property: return "property '" + member.Name + "'";
            case MemberKind.Field: return "field '" + member.Name + "'";
            case MemberKind.Event: return "event '" + member.Name + "'";
            case MemberKind.Nested: return "nested type '" + member.Name + "'";
            default: return "type";
        }
    }

    /// <summary>The plural member-kind noun used by <c>forbid public fields</c> style messages.</summary>
    internal static string MemberKindNoun(MemberKind kind)
    {
        switch (kind)
        {
            case MemberKind.Ctor: return "constructors";
            case MemberKind.Method: return "methods";
            case MemberKind.Property: return "properties";
            case MemberKind.Field: return "fields";
            case MemberKind.Event: return "events";
            case MemberKind.Nested: return "nested types";
            default: return "members";
        }
    }
}
