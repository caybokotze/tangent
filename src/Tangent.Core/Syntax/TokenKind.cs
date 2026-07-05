namespace Tangent.Core.Syntax;

/// <summary>The kind of a lexical token in a <c>.tan</c> file.</summary>
public enum TokenKind : byte
{
    /// <summary>End of file.</summary>
    EndOfFile = 0,

    /// <summary>A character the lexer could not classify.</summary>
    Unknown = 1,

    /// <summary>An identifier, possibly dotted (<c>Orders.Api.Controllers</c>).</summary>
    Identifier = 2,

    /// <summary>A dotted name containing <c>*</c> or <c>**</c> segments.</summary>
    Glob = 3,

    /// <summary>A double-quoted string literal, quotes included in the span.</summary>
    StringLiteral = 4,

    /// <summary>A name with one or more <c>{...}</c> template holes, for example <c>I{Name}</c>.</summary>
    Template = 5,

    /// <summary><c>*</c>.</summary>
    Star = 6,

    /// <summary><c>**</c>.</summary>
    DoubleStar = 7,

    /// <summary><c>{</c>.</summary>
    LeftBrace = 8,

    /// <summary><c>}</c>.</summary>
    RightBrace = 9,

    /// <summary><c>[</c>.</summary>
    LeftBracket = 10,

    /// <summary><c>]</c>.</summary>
    RightBracket = 11,

    /// <summary><c>(</c>.</summary>
    LeftParen = 12,

    /// <summary><c>)</c>.</summary>
    RightParen = 13,

    /// <summary><c>&lt;</c>.</summary>
    LessThan = 14,

    /// <summary><c>&gt;</c>.</summary>
    GreaterThan = 15,

    /// <summary><c>,</c>.</summary>
    Comma = 16,

    /// <summary><c>|</c>.</summary>
    Pipe = 17,

    /// <summary><c>:</c>.</summary>
    Colon = 18,

    /// <summary><c>-&gt;</c>.</summary>
    Arrow = 19,

    /// <summary><c>=</c>.</summary>
    Equals = 20,

    /// <summary><c>;</c>, an optional statement separator.</summary>
    Semicolon = 21,

    // ---- keywords ----

    /// <summary><c>project</c>.</summary>
    Project = 32,

    /// <summary><c>root</c>.</summary>
    Root = 33,

    /// <summary><c>layer</c>.</summary>
    Layer = 34,

    /// <summary><c>allow</c>.</summary>
    Allow = 35,

    /// <summary><c>contract</c>.</summary>
    Contract = 36,

    /// <summary><c>extends</c>.</summary>
    Extends = 37,

    /// <summary><c>match</c>.</summary>
    Match = 38,

    /// <summary><c>bind</c>.</summary>
    Bind = 39,

    /// <summary><c>exempt</c>.</summary>
    Exempt = 40,

    /// <summary><c>to</c>.</summary>
    To = 41,

    /// <summary><c>from</c>.</summary>
    From = 42,

    /// <summary><c>except</c>.</summary>
    Except = 43,

    /// <summary><c>reason</c>.</summary>
    Reason = 44,

    /// <summary><c>require</c>.</summary>
    Require = 45,

    /// <summary><c>require-each</c>.</summary>
    RequireEach = 46,

    /// <summary><c>forbid</c>.</summary>
    Forbid = 47,

    /// <summary><c>only</c>.</summary>
    Only = 48,

    /// <summary><c>in</c>.</summary>
    In = 49,

    /// <summary><c>where</c>.</summary>
    Where = 50,

    /// <summary><c>nothing</c>.</summary>
    Nothing = 51,

    /// <summary><c>using</c>.</summary>
    Using = 52,

    /// <summary><c>new</c>.</summary>
    New = 53,

    /// <summary><c>class</c>.</summary>
    Class = 54,

    /// <summary><c>interface</c>.</summary>
    Interface = 55,

    /// <summary><c>record</c>.</summary>
    Record = 56,

    /// <summary><c>struct</c>.</summary>
    Struct = 57,

    /// <summary><c>enum</c>.</summary>
    Enum = 58,

    /// <summary><c>any</c>.</summary>
    Any = 59,

    /// <summary><c>abstract</c>.</summary>
    Abstract = 60,

    /// <summary><c>sealed</c>.</summary>
    Sealed = 61,

    /// <summary><c>static</c>.</summary>
    Static = 62,

    /// <summary><c>partial</c>.</summary>
    Partial = 63,

    /// <summary><c>public</c>.</summary>
    Public = 64,

    /// <summary><c>private</c>.</summary>
    Private = 65,

    /// <summary><c>protected</c>.</summary>
    Protected = 66,

    /// <summary><c>internal</c>.</summary>
    Internal = 67,

    /// <summary><c>readonly</c>.</summary>
    Readonly = 68,

    /// <summary><c>get</c>.</summary>
    Get = 69,

    /// <summary><c>set</c>.</summary>
    Set = 70,

    /// <summary><c>init</c>.</summary>
    Init = 71,

    /// <summary><c>ctor</c>.</summary>
    Ctor = 72,

    /// <summary><c>methods</c>.</summary>
    Methods = 73,

    /// <summary><c>properties</c>.</summary>
    Properties = 74,

    /// <summary><c>fields</c>.</summary>
    Fields = 75,

    /// <summary><c>events</c>.</summary>
    Events = 76,

    /// <summary><c>nested</c>.</summary>
    Nested = 77,

    /// <summary><c>inherits</c>.</summary>
    Inherits = 78,

    /// <summary><c>implements</c>.</summary>
    Implements = 79,

    /// <summary><c>attributes</c>.</summary>
    Attributes = 80,

    /// <summary><c>returns</c>.</summary>
    Returns = 81,

    /// <summary><c>injects</c>.</summary>
    Injects = 82,

    /// <summary><c>lives</c>.</summary>
    Lives = 83,

    /// <summary><c>name</c>.</summary>
    Name = 84,

    /// <summary><c>namespace</c>.</summary>
    Namespace = 85,

    /// <summary><c>starts</c>.</summary>
    Starts = 86,

    /// <summary><c>ends</c>.</summary>
    Ends = 87,

    /// <summary><c>with</c>.</summary>
    With = 88,

    /// <summary><c>contains</c>.</summary>
    Contains = 89,

    /// <summary><c>matches</c>.</summary>
    Matches = 90,

    /// <summary><c>each</c>.</summary>
    Each = 91,

    /// <summary><c>parameter</c>.</summary>
    Parameter = 92,

    /// <summary><c>assigned</c>.</summary>
    Assigned = 93,

    /// <summary><c>field</c>.</summary>
    Field = 94,

    /// <summary><c>null-checked</c>.</summary>
    NullChecked = 95,

    /// <summary><c>not</c>.</summary>
    Not = 96,

    /// <summary><c>stored</c>.</summary>
    Stored = 97,

    /// <summary><c>stateless</c>.</summary>
    Stateless = 98,

    /// <summary><c>and</c>.</summary>
    And = 99,

    /// <summary><c>or</c>.</summary>
    Or = 100,
}
