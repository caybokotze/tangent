using System;

namespace Tangent.Core.Ir;

/// <summary>Whether a rule requires or forbids its predicate.</summary>
public enum Polarity : byte
{
    /// <summary>The predicate must hold.</summary>
    Require = 0,

    /// <summary>The predicate must not hold.</summary>
    Forbid = 1,
}

/// <summary>How expensive a predicate is to evaluate.</summary>
public enum RuleTier : byte
{
    /// <summary>Tier 0: evaluated from <c>ISymbol</c> alone.</summary>
    Symbol = 0,

    /// <summary>Tier 1: needs the semantic model of member bodies.</summary>
    Reference = 1,

    /// <summary>Tier 2: needs the operation tree.</summary>
    Flow = 2,
}

/// <summary>What a rule is evaluated against.</summary>
public enum MemberKind : byte
{
    /// <summary>The bound type itself.</summary>
    Type = 0,

    /// <summary>Instance and static constructors.</summary>
    Ctor = 1,

    /// <summary>Methods.</summary>
    Method = 2,

    /// <summary>Properties.</summary>
    Property = 3,

    /// <summary>Fields.</summary>
    Field = 4,

    /// <summary>Events.</summary>
    Event = 5,

    /// <summary>Nested types.</summary>
    Nested = 6,
}

/// <summary>The kind of C# type a <c>match</c> clause selects.</summary>
public enum TypeKind : byte
{
    /// <summary>Any type kind.</summary>
    Any = 0,

    /// <summary>A class.</summary>
    Class = 1,

    /// <summary>An interface.</summary>
    Interface = 2,

    /// <summary>A record (class or struct).</summary>
    Record = 3,

    /// <summary>A struct.</summary>
    Struct = 4,

    /// <summary>An enum.</summary>
    Enum = 5,
}

/// <summary>A property or event accessor.</summary>
public enum AccessorKind : byte
{
    /// <summary>The getter.</summary>
    Get = 0,

    /// <summary>The setter.</summary>
    Set = 1,

    /// <summary>The init-only setter.</summary>
    Init = 2,
}

/// <summary>C# modifiers and type-kind keywords usable as predicates and conditions.</summary>
[Flags]
public enum Modifiers : uint
{
    /// <summary>No modifier.</summary>
    None = 0,

    /// <summary><c>public</c>.</summary>
    Public = 1 << 0,

    /// <summary><c>private</c>.</summary>
    Private = 1 << 1,

    /// <summary><c>protected</c>.</summary>
    Protected = 1 << 2,

    /// <summary><c>internal</c>.</summary>
    Internal = 1 << 3,

    /// <summary><c>static</c>.</summary>
    Static = 1 << 4,

    /// <summary><c>sealed</c>.</summary>
    Sealed = 1 << 5,

    /// <summary><c>abstract</c>.</summary>
    Abstract = 1 << 6,

    /// <summary><c>readonly</c>.</summary>
    Readonly = 1 << 7,

    /// <summary><c>partial</c>.</summary>
    Partial = 1 << 8,

    /// <summary><c>class</c>.</summary>
    Class = 1 << 9,

    /// <summary><c>record</c>.</summary>
    Record = 1 << 10,

    /// <summary><c>struct</c>.</summary>
    Struct = 1 << 11,

    /// <summary><c>interface</c>.</summary>
    Interface = 1 << 12,

    /// <summary>All accessibility modifiers.</summary>
    AccessibilityMask = Public | Private | Protected | Internal,

    /// <summary>All type-kind keywords.</summary>
    TypeKindMask = Class | Record | Struct | Interface,
}

/// <summary>The discriminator of <see cref="Predicate"/>.</summary>
/// <summary>The string test of a <see cref="PredicateKind.NameIs"/> predicate or a name condition.</summary>
public enum NameOperator : byte
{
    /// <summary><c>name starts with "..."</c>.</summary>
    StartsWith = 0,

    /// <summary><c>name ends with "..."</c>.</summary>
    EndsWith = 1,

    /// <summary><c>name contains "..."</c>.</summary>
    Contains = 2,

    /// <summary><c>name matches "..."</c>, a regular expression.</summary>
    Matches = 3,
}

public enum PredicateKind : byte
{
    /// <summary>The selected entity carries the given modifiers. Payload is a <see cref="Modifiers"/> mask.</summary>
    Modifier = 0,

    /// <summary>Inherits one of a group of type patterns. Payload is a type-pattern group index.</summary>
    Inherits = 1,

    /// <summary>Implements one of a group of type patterns. Payload is a type-pattern group index.</summary>
    Implements = 2,

    /// <summary>Carries an attribute matching a group. Payload is an attribute-group index.</summary>
    HasAttributes = 3,

    /// <summary>Returns one of a group of type patterns. Payload is a type-pattern group index.</summary>
    Returns = 4,

    /// <summary>One constructor parameter matches the group. Payload is a type-pattern group index.</summary>
    Injects = 5,

    /// <summary>Every constructor parameter matches the group. Payload is a type-pattern group index.</summary>
    InjectsOnly = 6,

    /// <summary>References a namespace matching a glob. Payload is a glob index.</summary>
    UsingNamespace = 7,

    /// <summary>Creates an instance of one of a group of type patterns. Payload is a type-pattern group index.</summary>
    NewExpression = 8,

    /// <summary>Declares an accessor. Payload is an <see cref="AccessorKind"/>, Payload2 a <see cref="Modifiers"/> mask.</summary>
    Accessor = 9,

    /// <summary>The templated companion interface lives in a layer. Payload is a template index, Payload2 a layer index.</summary>
    InterfaceLivesIn = 10,

    /// <summary>Declares members of a kind. Payload is a <see cref="MemberKind"/>, Payload2 a <see cref="Modifiers"/> mask.</summary>
    HasMemberKind = 11,

    /// <summary>Every constructor parameter is assigned to a field. Payload is a <see cref="Modifiers"/> mask for the field.</summary>
    EachParamAssignedToField = 12,

    /// <summary>Every constructor parameter is null-checked.</summary>
    EachParamNullChecked = 13,

    /// <summary>No constructor parameter is stored.</summary>
    EachParamNotStored = 14,

    /// <summary>The type holds no mutable instance state.</summary>
    Stateless = 15,

    /// <summary>
    /// Every interface the type implements directly must match the group. Payload is a type-pattern
    /// group index.
    /// </summary>
    ImplementsOnly = 16,

    /// <summary>
    /// The type's base type, when it has one other than <c>object</c>, must match the group. Payload is
    /// a type-pattern group index.
    /// </summary>
    InheritsOnly = 17,

    /// <summary>
    /// The name must satisfy a string test. Payload is a <see cref="NameOperator"/>, Payload2 a
    /// <c>StringId</c> holding the operand.
    /// </summary>
    NameIs = 18,

    /// <summary>
    /// The type must live in a layer or a namespace glob. Payload is a layer index or -1, Payload2 a
    /// glob index or -1.
    /// </summary>
    LivesIn = 19,

    /// <summary>
    /// A reference to a type bound to another contract. Payload is a contract index.
    /// </summary>
    UsingContract = 20,
}

/// <summary>The discriminator of <see cref="Condition"/>.</summary>
public enum ConditionKind : byte
{
    /// <summary>Both operands hold. Left and Right are condition indices.</summary>
    And = 0,

    /// <summary>Either operand holds. Left and Right are condition indices.</summary>
    Or = 1,

    /// <summary>The name starts with a string. Payload is a <c>StringId</c>.</summary>
    NameStartsWith = 2,

    /// <summary>The name ends with a string. Payload is a <c>StringId</c>.</summary>
    NameEndsWith = 3,

    /// <summary>The name contains a string. Payload is a <c>StringId</c>.</summary>
    NameContains = 4,

    /// <summary>The name matches a regular expression. Payload is a <c>StringId</c>.</summary>
    NameMatches = 5,

    /// <summary>Inherits a type pattern group. Payload is a type-pattern group index.</summary>
    Inherits = 6,

    /// <summary>Implements a type pattern group. Payload is a type-pattern group index.</summary>
    Implements = 7,

    /// <summary>Carries an attribute group. Payload is an attribute-group index.</summary>
    HasAttributes = 8,

    /// <summary>Carries the given modifiers. Payload is a <see cref="Modifiers"/> mask.</summary>
    Modifier = 9,

    /// <summary>The operand does not hold. Left is the negated condition index.</summary>
    Not = 10,
}
