namespace Tangent.LanguageServer.Syntax;

/// <summary>What a classified region of a <c>.tan</c> file means.</summary>
internal enum TanRegionKind
{
    /// <summary>A reserved word.</summary>
    Keyword,

    /// <summary>Punctuation with meaning: <c>-&gt;</c>, <c>|</c>, <c>,</c>, <c>=</c>, <c>:</c>, <c>&lt;</c>, <c>&gt;</c>, <c>*</c>.</summary>
    Operator,

    /// <summary>A double-quoted string.</summary>
    StringLiteral,

    /// <summary>A line or block comment.</summary>
    Comment,

    /// <summary>The name in <c>contract X</c>.</summary>
    ContractDeclaration,

    /// <summary>A contract name used in <c>extends</c>, <c>bind ... to</c> or <c>ignore ... from</c>.</summary>
    ContractReference,

    /// <summary>The name in <c>layer X = ...</c>.</summary>
    LayerDeclaration,

    /// <summary>A layer name used in a dependency line, a <c>match</c> clause or <c>lives in</c>.</summary>
    LayerReference,

    /// <summary>A namespace glob such as <c>Orders.Domain.**</c>.</summary>
    Glob,

    /// <summary>A C# type pattern such as <c>Task&lt;ActionResult&lt;*&gt;&gt;</c>; the region covers only the name.</summary>
    TypePattern,

    /// <summary>A name template such as <c>I{Name}</c>.</summary>
    TypeTemplate,

    /// <summary>An attribute name inside <c>[ ]</c> or after <c>except</c>.</summary>
    AttributeName,

    /// <summary>The fully qualified type name in a <c>bind</c> or <c>ignore</c> statement.</summary>
    BoundTypeName,
}

/// <summary>One classified stretch of a <c>.tan</c> file.</summary>
/// <param name="Start">Character offset of the first character.</param>
/// <param name="Length">Number of characters covered.</param>
/// <param name="Kind">What the stretch means.</param>
/// <param name="Text">The covered text.</param>
internal readonly record struct TanRegion(int Start, int Length, TanRegionKind Kind, string Text)
{
    /// <summary>The offset one past the last character.</summary>
    public int End => Start + Length;

    /// <summary>True when <paramref name="offset"/> falls inside the region, or immediately after it.</summary>
    public bool Touches(int offset) => offset >= Start && offset <= End;

    /// <summary>True when the region names a contract.</summary>
    public bool IsContract => Kind is TanRegionKind.ContractDeclaration or TanRegionKind.ContractReference;

    /// <summary>True when the region names a layer.</summary>
    public bool IsLayer => Kind is TanRegionKind.LayerDeclaration or TanRegionKind.LayerReference;

    /// <summary>True when the region names a C# type.</summary>
    public bool IsTypeName => Kind is TanRegionKind.TypePattern or TanRegionKind.BoundTypeName;
}
