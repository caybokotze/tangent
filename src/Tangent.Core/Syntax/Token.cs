using System;

namespace Tangent.Core.Syntax;

/// <summary>One lexical token: a kind plus a position in the file. Text is recovered from the source on demand.</summary>
public readonly record struct Token(TokenKind Kind, int Start, int Length, int Line, int Column)
{
    /// <summary>The offset one past the last character of the token.</summary>
    public int End => Start + Length;

    /// <summary>The token text, taken from the file without allocating.</summary>
    public ReadOnlySpan<char> Text(ReadOnlySpan<char> source) => source.Slice(Start, Length);
}

/// <summary>Helpers for describing token kinds in messages and goldens.</summary>
public static class TokenKinds
{
    /// <summary>True when the kind is a reserved word.</summary>
    public static bool IsKeyword(TokenKind kind) => kind >= TokenKind.Project;

    /// <summary>True when the kind names a member block (<c>ctor</c>, <c>methods</c>, ...).</summary>
    public static bool IsMemberKind(TokenKind kind) =>
        kind == TokenKind.Ctor || kind == TokenKind.Methods || kind == TokenKind.Properties ||
        kind == TokenKind.Fields || kind == TokenKind.Events || kind == TokenKind.Nested;

    /// <summary>True when the kind is a C# modifier or type-kind keyword usable as a predicate.</summary>
    public static bool IsModifier(TokenKind kind)
    {
        switch (kind)
        {
            case TokenKind.Public:
            case TokenKind.Private:
            case TokenKind.Protected:
            case TokenKind.Internal:
            case TokenKind.Static:
            case TokenKind.Sealed:
            case TokenKind.Abstract:
            case TokenKind.Readonly:
            case TokenKind.Partial:
            case TokenKind.Class:
            case TokenKind.Record:
            case TokenKind.Struct:
            case TokenKind.Interface:
                return true;
            default:
                return false;
        }
    }

    /// <summary>The canonical source text of a token kind, for error messages. Returns a description for non-fixed kinds.</summary>
    public static string Describe(TokenKind kind)
    {
        switch (kind)
        {
            case TokenKind.EndOfFile: return "end of file";
            case TokenKind.Unknown: return "unrecognised character";
            case TokenKind.Identifier: return "identifier";
            case TokenKind.Glob: return "namespace glob";
            case TokenKind.StringLiteral: return "string";
            case TokenKind.Template: return "template";
            case TokenKind.Star: return "'*'";
            case TokenKind.DoubleStar: return "'**'";
            case TokenKind.LeftBrace: return "'{'";
            case TokenKind.RightBrace: return "'}'";
            case TokenKind.LeftBracket: return "'['";
            case TokenKind.RightBracket: return "']'";
            case TokenKind.LeftParen: return "'('";
            case TokenKind.RightParen: return "')'";
            case TokenKind.LessThan: return "'<'";
            case TokenKind.GreaterThan: return "'>'";
            case TokenKind.Comma: return "','";
            case TokenKind.Pipe: return "'|'";
            case TokenKind.Colon: return "':'";
            case TokenKind.Semicolon: return "';'";
            case TokenKind.Arrow: return "'->'";
            case TokenKind.Equals: return "'='";
            default:
                string? text = KeywordText(kind);
                return text is null ? kind.ToString() : "'" + text + "'";
        }
    }

    /// <summary>The spelling of a keyword token kind, or null when the kind is not a keyword.</summary>
    public static string? KeywordText(TokenKind kind)
    {
        switch (kind)
        {
            case TokenKind.Project: return "project";
            case TokenKind.Root: return "root";
            case TokenKind.Layer: return "layer";
            case TokenKind.Allow: return "allow";
            case TokenKind.Contract: return "contract";
            case TokenKind.Extends: return "extends";
            case TokenKind.Match: return "match";
            case TokenKind.Bind: return "bind";
            case TokenKind.Exempt: return "exempt";
            case TokenKind.To: return "to";
            case TokenKind.From: return "from";
            case TokenKind.Except: return "except";
            case TokenKind.Reason: return "reason";
            case TokenKind.Require: return "require";
            case TokenKind.RequireEach: return "require-each";
            case TokenKind.Forbid: return "forbid";
            case TokenKind.Only: return "only";
            case TokenKind.In: return "in";
            case TokenKind.Where: return "where";
            case TokenKind.Nothing: return "nothing";
            case TokenKind.Using: return "using";
            case TokenKind.New: return "new";
            case TokenKind.Class: return "class";
            case TokenKind.Interface: return "interface";
            case TokenKind.Record: return "record";
            case TokenKind.Struct: return "struct";
            case TokenKind.Enum: return "enum";
            case TokenKind.Any: return "any";
            case TokenKind.Abstract: return "abstract";
            case TokenKind.Sealed: return "sealed";
            case TokenKind.Static: return "static";
            case TokenKind.Partial: return "partial";
            case TokenKind.Public: return "public";
            case TokenKind.Private: return "private";
            case TokenKind.Protected: return "protected";
            case TokenKind.Internal: return "internal";
            case TokenKind.Readonly: return "readonly";
            case TokenKind.Get: return "get";
            case TokenKind.Set: return "set";
            case TokenKind.Init: return "init";
            case TokenKind.Ctor: return "ctor";
            case TokenKind.Methods: return "methods";
            case TokenKind.Properties: return "properties";
            case TokenKind.Fields: return "fields";
            case TokenKind.Events: return "events";
            case TokenKind.Nested: return "nested";
            case TokenKind.Inherits: return "inherits";
            case TokenKind.Implements: return "implements";
            case TokenKind.Attributes: return "attributes";
            case TokenKind.Returns: return "returns";
            case TokenKind.Injects: return "injects";
            case TokenKind.Lives: return "lives";
            case TokenKind.Name: return "name";
            case TokenKind.Namespace: return "namespace";
            case TokenKind.Starts: return "starts";
            case TokenKind.Ends: return "ends";
            case TokenKind.With: return "with";
            case TokenKind.Contains: return "contains";
            case TokenKind.Matches: return "matches";
            case TokenKind.Each: return "each";
            case TokenKind.Parameter: return "parameter";
            case TokenKind.Assigned: return "assigned";
            case TokenKind.Field: return "field";
            case TokenKind.NullChecked: return "null-checked";
            case TokenKind.Not: return "not";
            case TokenKind.Stored: return "stored";
            case TokenKind.Stateless: return "stateless";
            case TokenKind.And: return "and";
            case TokenKind.Or: return "or";
            default: return null;
        }
    }
}
