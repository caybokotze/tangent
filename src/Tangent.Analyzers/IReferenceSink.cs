using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Tangent.Analyzers;

/// <summary>How one type came to reference another.</summary>
public enum ReferenceKind : byte
{
    /// <summary>The referencing type derives from the referenced type.</summary>
    BaseType = 0,

    /// <summary>The referencing type implements the referenced interface.</summary>
    Interface = 1,

    /// <summary>The referenced type is used as an attribute.</summary>
    Attribute = 2,

    /// <summary>The referenced type appears in a member signature: return type, parameter, field or property type.</summary>
    MemberSignature = 3,

    /// <summary>A method on the referenced type is invoked.</summary>
    Invocation = 4,

    /// <summary>A field, property or event of the referenced type is accessed.</summary>
    MemberAccess = 5,

    /// <summary>The referenced type is instantiated.</summary>
    ObjectCreation = 6,

    /// <summary>The referenced type appears in a <c>typeof</c> expression.</summary>
    TypeOf = 7,

    /// <summary>Anything else the walk observed.</summary>
    Other = 8,
}

/// <summary>
/// Receives every type-to-type reference the Tier 1 walk observes. The graph exporter registers one of
/// these so <c>tangent graph</c> reuses the analyzer's walk instead of doing its own.
/// </summary>
public interface IReferenceSink
{
    /// <summary>
    /// Called once per named type the analyzer looks at: bound types always, and every type when the
    /// analyzer was created with <c>includeUnbound</c>.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="contractNames">The contracts bound to it; empty when it is bound to nothing.</param>
    /// <param name="layerIndex">The index of the layer its namespace falls in, or null when it is in none.</param>
    /// <param name="layerName">The name of that layer, or null.</param>
    void OnTypeBound(INamedTypeSymbol type, ImmutableArray<string> contractNames, int? layerIndex, string? layerName);

    /// <summary>Called for every observed reference from one named type to another.</summary>
    /// <param name="from">The referencing type.</param>
    /// <param name="to">The referenced type.</param>
    /// <param name="kind">How the reference was made.</param>
    /// <param name="location">Where the reference appears.</param>
    /// <param name="violatesLayering">True when the layer matrix forbids this reference.</param>
    void OnReference(INamedTypeSymbol from, INamedTypeSymbol to, ReferenceKind kind, Location location, bool violatesLayering);
}
