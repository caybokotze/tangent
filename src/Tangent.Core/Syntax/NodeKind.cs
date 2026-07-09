namespace Tangent.Core.Syntax;

/// <summary>The kind of an AST node produced by the parser.</summary>
public enum NodeKind : byte
{
    /// <summary>The whole file. Children are declarations.</summary>
    File = 0,

    /// <summary>A node the parser could not make sense of.</summary>
    Error = 1,

    // ---- project ----

    /// <summary><c>project "Name" { ... }</c>. Payload is the name <c>StringId</c>.</summary>
    Project = 2,

    /// <summary><c>root: "src/"</c>. Payload is the path <c>StringId</c>.</summary>
    ProjectRoot = 3,

    /// <summary><c>layer Name = glob</c>. Payload is the name, Payload2 the glob text, both <c>StringId</c>.</summary>
    LayerDecl = 4,

    /// <summary><c>A -&gt; B, C</c>. Payload is the source layer name <c>StringId</c>; children are targets.</summary>
    Dependency = 5,

    /// <summary>A layer name in a dependency list or a match clause. Payload is a <c>StringId</c>.</summary>
    LayerRef = 6,

    /// <summary><c>nothing</c> on the right of an arrow.</summary>
    Nothing = 7,

    /// <summary><c>allow glob, glob</c>. Children are <see cref="GlobRef"/> nodes.</summary>
    Allow = 8,

    /// <summary>A namespace glob. Payload is the glob text <c>StringId</c>.</summary>
    GlobRef = 9,

    // ---- contracts ----

    /// <summary><c>contract Name [extends Base] { ... }</c>. Payload is the name, Payload2 the base name or -1.</summary>
    Contract = 10,

    /// <summary><c>match: kind in target [where cond]</c>. Payload is a <c>TypeKind</c>.</summary>
    Match = 11,

    /// <summary>A member block. Payload is a <c>MemberKind</c>; first child may be a condition.</summary>
    MemberBlock = 12,

    /// <summary><c>where cond { ... }</c>. First child is the condition, the rest are rules.</summary>
    Refinement = 13,

    /// <summary>A rule. Payload is a <c>Polarity</c>; the single child is the predicate.</summary>
    Rule = 14,

    // ---- predicates ----

    /// <summary>Modifier predicate. Payload is a <c>Modifiers</c> mask.</summary>
    PredModifier = 20,

    /// <summary><c>inherits</c>. The single child is a <see cref="TypeAlt"/>.</summary>
    PredInherits = 21,

    /// <summary><c>implements</c>. The single child is a <see cref="TypeAlt"/>.</summary>
    PredImplements = 22,

    /// <summary><c>attributes [..]</c>. The single child is an <see cref="AttrGroup"/>.</summary>
    PredAttributes = 23,

    /// <summary><c>returns</c>. The single child is a <see cref="TypeAlt"/>.</summary>
    PredReturns = 24,

    /// <summary><c>injects</c>. The single child is a <see cref="TypeAlt"/>.</summary>
    PredInjects = 25,

    /// <summary><c>injects only</c>. The single child is a <see cref="TypeAlt"/> listing everything allowed.</summary>
    PredInjectsOnly = 26,

    /// <summary><c>using glob</c>. Payload is the glob text <c>StringId</c>.</summary>
    PredUsing = 27,

    /// <summary><c>new Type</c>. The single child is a <see cref="TypeAlt"/>.</summary>
    PredNew = 28,

    /// <summary>Accessor predicate. Payload is an <c>AccessorKind</c>, Payload2 a <c>Modifiers</c> mask.</summary>
    PredAccessor = 29,

    /// <summary><c>interface I{Name} lives in Layer</c>. Payload is the template text, Payload2 the layer name.</summary>
    PredInterfaceLivesIn = 30,

    /// <summary>Member-kind predicate. Payload is a <c>MemberKind</c>, Payload2 a <c>Modifiers</c> mask.</summary>
    PredMemberKind = 31,

    /// <summary><c>each parameter assigned to [modifiers] field</c>. Payload is a <c>Modifiers</c> mask.</summary>
    PredEachParamAssignedToField = 32,

    /// <summary><c>each parameter null-checked</c>.</summary>
    PredEachParamNullChecked = 33,

    /// <summary><c>each parameter not stored</c>.</summary>
    PredEachParamNotStored = 34,

    /// <summary><c>stateless</c>.</summary>
    PredStateless = 35,

    // ---- type patterns and attributes ----

    /// <summary>An alternation or list of type patterns. Children are <see cref="TypePattern"/> nodes.</summary>
    TypeAlt = 40,

    /// <summary>A type pattern. Payload is the name <c>StringId</c>; children are type arguments.</summary>
    TypePattern = 41,

    /// <summary>A bare <c>*</c> type pattern.</summary>
    TypeWildcard = 42,

    /// <summary>A type pattern whose name is a template. Payload is the template text <c>StringId</c>.</summary>
    TypeTemplate = 43,

    /// <summary>One <c>[A | B]</c> attribute group. Children are <see cref="AttrRef"/> nodes.</summary>
    AttrGroup = 44,

    /// <summary>One attribute name. Payload is the name <c>StringId</c>, Payload2 is 1 when <c>(*)</c> was written.</summary>
    AttrRef = 45,

    // ---- conditions ----

    /// <summary><c>and</c>. Two children.</summary>
    CondAnd = 50,

    /// <summary><c>or</c>. Two children.</summary>
    CondOr = 51,

    /// <summary><c>name starts with "s"</c>. Payload is the string <c>StringId</c>.</summary>
    CondNameStartsWith = 52,

    /// <summary><c>name ends with "s"</c>. Payload is the string <c>StringId</c>.</summary>
    CondNameEndsWith = 53,

    /// <summary><c>name contains "s"</c>. Payload is the string <c>StringId</c>.</summary>
    CondNameContains = 54,

    /// <summary><c>name matches "re"</c>. Payload is the string <c>StringId</c>.</summary>
    CondNameMatches = 55,

    /// <summary><c>inherits</c> in a condition. The single child is a <see cref="TypeAlt"/>.</summary>
    CondInherits = 56,

    /// <summary><c>implements</c> in a condition. The single child is a <see cref="TypeAlt"/>.</summary>
    CondImplements = 57,

    /// <summary><c>attributes</c> in a condition. The single child is an <see cref="AttrGroup"/>.</summary>
    CondAttributes = 58,

    /// <summary>A bare modifier in a condition. Payload is a <c>Modifiers</c> mask.</summary>
    CondModifier = 59,

    // ---- bind / exempt ----

    /// <summary><c>bind Type to A, B [except ...]</c>. Payload is the type name <c>StringId</c>.</summary>
    Bind = 60,

    /// <summary>A contract name reference. Payload is a <c>StringId</c>.</summary>
    ContractRef = 61,

    /// <summary>An attribute excluded by <c>except</c>. Payload is the attribute name <c>StringId</c>.</summary>
    ExceptAttribute = 62,

    /// <summary>A rule id excluded by <c>except</c>. Payload is the rule id <c>StringId</c>.</summary>
    ExceptRule = 63,

    /// <summary><c>exempt Type [from A, B] reason "..."</c>. Payload is the type name, Payload2 the reason.</summary>
    Exempt = 64,
}
