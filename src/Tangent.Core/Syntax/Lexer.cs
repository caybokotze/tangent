using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using Tangent.Core.Diagnostics;
using Tangent.Core.Text;

namespace Tangent.Core.Syntax;

/// <summary>Turns the text of a <c>.tan</c> file into a flat token array. Lexical errors become diagnostics, never exceptions.</summary>
public static class Lexer
{
#if NET10_0_OR_GREATER
    private static readonly SearchValues<char> IdentifierParts = SearchValues.Create(
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_");
#endif

    /// <summary>Lexes <paramref name="text"/>, appending any lexical diagnostics to <paramref name="diagnostics"/>.</summary>
    public static Token[] Lex(ReadOnlySpan<char> text, int fileIndex, string? filePath, List<TangentDiagnostic> diagnostics)
    {
        Token[] buffer = ArrayPool<Token>.Shared.Rent(Math.Max(16, text.Length / 4 + 16));
        int count = 0;
        int pos = 0;
        int line = 1;
        int lineStart = 0;

        try
        {
            while (true)
            {
                // Skip whitespace and comments.
                while (pos < text.Length)
                {
                    char c = text[pos];
                    if (c == '\n')
                    {
                        pos++;
                        line++;
                        lineStart = pos;
                    }
                    else if (c == '\r')
                    {
                        pos++;
                        if (pos < text.Length && text[pos] == '\n') pos++;
                        line++;
                        lineStart = pos;
                    }
                    else if (c == ' ' || c == '\t' || c == '\f' || c == '\v')
                    {
                        pos++;
                    }
                    else if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '/')
                    {
                        pos += 2;
                        while (pos < text.Length && text[pos] != '\n' && text[pos] != '\r') pos++;
                    }
                    else if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '*')
                    {
                        int commentStart = pos;
                        int commentLine = line;
                        int commentColumn = pos - lineStart + 1;
                        pos += 2;
                        bool closed = false;
                        while (pos < text.Length)
                        {
                            if (text[pos] == '*' && pos + 1 < text.Length && text[pos + 1] == '/')
                            {
                                pos += 2;
                                closed = true;
                                break;
                            }

                            if (text[pos] == '\n')
                            {
                                line++;
                                pos++;
                                lineStart = pos;
                            }
                            else if (text[pos] == '\r')
                            {
                                pos++;
                                if (pos < text.Length && text[pos] == '\n') pos++;
                                line++;
                                lineStart = pos;
                            }
                            else
                            {
                                pos++;
                            }
                        }

                        if (!closed)
                        {
                            Report(diagnostics, fileIndex, filePath, commentStart, pos - commentStart, commentLine, commentColumn,
                                "unterminated block comment");
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                int column = pos - lineStart + 1;
                if (pos >= text.Length)
                {
                    Append(ref buffer, ref count, new Token(TokenKind.EndOfFile, text.Length, 0, line, column));
                    break;
                }

                int start = pos;
                char ch = text[pos];

                if (IsIdentifierStart(ch))
                {
                    pos = ScanIdentifierChars(text, pos + 1);

                    // Hyphenated keywords: null-checked, require-each.
                    while (pos + 1 < text.Length && text[pos] == '-' && IsIdentifierStart(text[pos + 1]))
                    {
                        pos = ScanIdentifierChars(text, pos + 1);
                    }

                    if (pos < text.Length && text[pos] == '{')
                    {
                        // A template is a name with {...} holes attached directly to it.
                        if (ScanTemplate(text, ref pos))
                        {
                            Append(ref buffer, ref count, new Token(TokenKind.Template, start, pos - start, line, column));
                        }
                        else
                        {
                            Report(diagnostics, fileIndex, filePath, start, pos - start, line, column,
                                "unterminated template; expected '}'");
                            Append(ref buffer, ref count, new Token(TokenKind.Identifier, start, pos - start, line, column));
                        }

                        continue;
                    }

                    bool hasStar = false;
                    while (pos + 1 < text.Length && text[pos] == '.')
                    {
                        char next = text[pos + 1];
                        if (IsIdentifierStart(next))
                        {
                            pos = ScanIdentifierChars(text, pos + 2);
                        }
                        else if (next == '*')
                        {
                            hasStar = true;
                            pos += 2;
                            if (pos < text.Length && text[pos] == '*') pos++;
                        }
                        else
                        {
                            break;
                        }
                    }

                    int length = pos - start;
                    TokenKind kind;
                    if (hasStar)
                    {
                        kind = TokenKind.Glob;
                    }
                    else
                    {
                        ReadOnlySpan<char> word = text.Slice(start, length);
                        kind = word.IndexOf('.') >= 0 ? TokenKind.Identifier : LookupKeyword(word);
                    }

                    Append(ref buffer, ref count, new Token(kind, start, length, line, column));
                    continue;
                }

                switch (ch)
                {
                    case '"':
                    {
                        pos++;
                        bool terminated = false;
                        while (pos < text.Length)
                        {
                            char s = text[pos];
                            if (s == '\\')
                            {
                                if (pos + 1 < text.Length && (text[pos + 1] == '"' || text[pos + 1] == '\\'))
                                {
                                    pos += 2;
                                    continue;
                                }

                                Report(diagnostics, fileIndex, filePath, pos, 1, line, pos - lineStart + 1,
                                    "unsupported escape sequence; only \\\" and \\\\ are allowed");
                                pos++;
                                continue;
                            }

                            if (s == '"')
                            {
                                pos++;
                                terminated = true;
                                break;
                            }

                            if (s == '\n' || s == '\r') break;
                            pos++;
                        }

                        if (!terminated)
                        {
                            Report(diagnostics, fileIndex, filePath, start, pos - start, line, column,
                                "unterminated string literal");
                        }

                        Append(ref buffer, ref count, new Token(TokenKind.StringLiteral, start, pos - start, line, column));
                        continue;
                    }

                    case '*':
                        pos++;
                        if (pos < text.Length && text[pos] == '*')
                        {
                            pos++;
                            Append(ref buffer, ref count, new Token(TokenKind.DoubleStar, start, 2, line, column));
                        }
                        else
                        {
                            Append(ref buffer, ref count, new Token(TokenKind.Star, start, 1, line, column));
                        }

                        continue;

                    case '-':
                        if (pos + 1 < text.Length && text[pos + 1] == '>')
                        {
                            pos += 2;
                            Append(ref buffer, ref count, new Token(TokenKind.Arrow, start, 2, line, column));
                            continue;
                        }

                        pos++;
                        Report(diagnostics, fileIndex, filePath, start, 1, line, column, "unexpected '-'; did you mean '->'?");
                        Append(ref buffer, ref count, new Token(TokenKind.Unknown, start, 1, line, column));
                        continue;

                    default:
                    {
                        TokenKind punct = LookupPunctuation(ch);
                        if (punct != TokenKind.Unknown)
                        {
                            pos++;
                            Append(ref buffer, ref count, new Token(punct, start, 1, line, column));
                            continue;
                        }

                        pos++;
                        Report(diagnostics, fileIndex, filePath, start, 1, line, column,
                            "unexpected character '" + ch + "'");
                        Append(ref buffer, ref count, new Token(TokenKind.Unknown, start, 1, line, column));
                        continue;
                    }
                }
            }

            var result = new Token[count];
            Array.Copy(buffer, result, count);
            return result;
        }
        finally
        {
            ArrayPool<Token>.Shared.Return(buffer);
        }
    }

    /// <summary>Returns the characters inside a string literal token, quotes removed, escapes left intact.</summary>
    public static ReadOnlySpan<char> StringContent(ReadOnlySpan<char> raw)
    {
        if (raw.Length == 0 || raw[0] != '"') return raw;
        int end = raw.Length;
        if (end >= 2 && raw[end - 1] == '"' && !IsEscapedQuote(raw, end - 1)) end--;
        return raw.Slice(1, end - 1);
    }

    /// <summary>True when the content of a string literal contains escape sequences that need decoding.</summary>
    public static bool NeedsUnescaping(ReadOnlySpan<char> content) => content.IndexOf('\\') >= 0;

    /// <summary>Decodes <c>\"</c> and <c>\\</c> in the content of a string literal.</summary>
    public static string Unescape(ReadOnlySpan<char> content)
    {
        var sb = new StringBuilder(content.Length);
        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];
            if (c == '\\' && i + 1 < content.Length && (content[i + 1] == '"' || content[i + 1] == '\\'))
            {
                sb.Append(content[i + 1]);
                i++;
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static bool IsEscapedQuote(ReadOnlySpan<char> raw, int index)
    {
        int backslashes = 0;
        for (int i = index - 1; i >= 0 && raw[i] == '\\'; i--) backslashes++;
        return (backslashes & 1) == 1;
    }

    private static bool ScanTemplate(ReadOnlySpan<char> text, ref int pos)
    {
        while (pos < text.Length)
        {
            char c = text[pos];
            if (c == '{')
            {
                int close = -1;
                for (int i = pos + 1; i < text.Length; i++)
                {
                    if (text[i] == '}') { close = i; break; }
                    if (text[i] == '\n' || text[i] == '\r' || text[i] == '{') break;
                }

                if (close < 0) return false;
                pos = close + 1;
            }
            else if (IsIdentifierPart(c))
            {
                pos = ScanIdentifierChars(text, pos + 1);
            }
            else
            {
                break;
            }
        }

        return true;
    }

    private static int ScanIdentifierChars(ReadOnlySpan<char> text, int pos)
    {
#if NET10_0_OR_GREATER
        if (pos >= text.Length) return pos;
        int relative = text.Slice(pos).IndexOfAnyExcept(IdentifierParts);
        return relative < 0 ? text.Length : pos + relative;
#else
        while (pos < text.Length && IsIdentifierPart(text[pos])) pos++;
        return pos;
#endif
    }

    private static bool IsIdentifierStart(char c) =>
        (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';

    private static bool IsIdentifierPart(char c) =>
        (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';

    private static TokenKind LookupPunctuation(char c)
    {
        switch (c)
        {
            case '{': return TokenKind.LeftBrace;
            case '}': return TokenKind.RightBrace;
            case '[': return TokenKind.LeftBracket;
            case ']': return TokenKind.RightBracket;
            case '(': return TokenKind.LeftParen;
            case ')': return TokenKind.RightParen;
            case '<': return TokenKind.LessThan;
            case '>': return TokenKind.GreaterThan;
            case ',': return TokenKind.Comma;
            case '|': return TokenKind.Pipe;
            case ':': return TokenKind.Colon;
            case ';': return TokenKind.Semicolon;
            case '=': return TokenKind.Equals;
            default: return TokenKind.Unknown;
        }
    }

    /// <summary>Classifies a single, undotted word as a keyword or as an identifier.</summary>
    public static TokenKind LookupKeyword(ReadOnlySpan<char> word)
    {
        switch (word.Length)
        {
            case 2:
                if (word.SequenceEqual("to".AsSpan())) return TokenKind.To;
                if (word.SequenceEqual("in".AsSpan())) return TokenKind.In;
                if (word.SequenceEqual("or".AsSpan())) return TokenKind.Or;
                break;
            case 3:
                if (word.SequenceEqual("new".AsSpan())) return TokenKind.New;
                if (word.SequenceEqual("get".AsSpan())) return TokenKind.Get;
                if (word.SequenceEqual("set".AsSpan())) return TokenKind.Set;
                if (word.SequenceEqual("and".AsSpan())) return TokenKind.And;
                if (word.SequenceEqual("any".AsSpan())) return TokenKind.Any;
                if (word.SequenceEqual("not".AsSpan())) return TokenKind.Not;
                break;
            case 4:
                if (word.SequenceEqual("root".AsSpan())) return TokenKind.Root;
                if (word.SequenceEqual("bind".AsSpan())) return TokenKind.Bind;
                if (word.SequenceEqual("only".AsSpan())) return TokenKind.Only;
                if (word.SequenceEqual("from".AsSpan())) return TokenKind.From;
                if (word.SequenceEqual("with".AsSpan())) return TokenKind.With;
                if (word.SequenceEqual("name".AsSpan())) return TokenKind.Name;
                if (word.SequenceEqual("each".AsSpan())) return TokenKind.Each;
                if (word.SequenceEqual("init".AsSpan())) return TokenKind.Init;
                if (word.SequenceEqual("ends".AsSpan())) return TokenKind.Ends;
                if (word.SequenceEqual("ctor".AsSpan())) return TokenKind.Ctor;
                if (word.SequenceEqual("enum".AsSpan())) return TokenKind.Enum;
                break;
            case 5:
                if (word.SequenceEqual("layer".AsSpan())) return TokenKind.Layer;
                if (word.SequenceEqual("match".AsSpan())) return TokenKind.Match;
                if (word.SequenceEqual("where".AsSpan())) return TokenKind.Where;
                if (word.SequenceEqual("using".AsSpan())) return TokenKind.Using;
                if (word.SequenceEqual("class".AsSpan())) return TokenKind.Class;
                if (word.SequenceEqual("field".AsSpan())) return TokenKind.Field;
                if (word.SequenceEqual("lives".AsSpan())) return TokenKind.Lives;
                if (word.SequenceEqual("allow".AsSpan())) return TokenKind.Allow;
                break;
            case 6:
                if (word.SequenceEqual("except".AsSpan())) return TokenKind.Except;
                if (word.SequenceEqual("exempt".AsSpan())) return TokenKind.Exempt;
                if (word.SequenceEqual("events".AsSpan())) return TokenKind.Events;
                if (word.SequenceEqual("reason".AsSpan())) return TokenKind.Reason;
                if (word.SequenceEqual("record".AsSpan())) return TokenKind.Record;
                if (word.SequenceEqual("forbid".AsSpan())) return TokenKind.Forbid;
                if (word.SequenceEqual("fields".AsSpan())) return TokenKind.Fields;
                if (word.SequenceEqual("struct".AsSpan())) return TokenKind.Struct;
                if (word.SequenceEqual("sealed".AsSpan())) return TokenKind.Sealed;
                if (word.SequenceEqual("static".AsSpan())) return TokenKind.Static;
                if (word.SequenceEqual("starts".AsSpan())) return TokenKind.Starts;
                if (word.SequenceEqual("stored".AsSpan())) return TokenKind.Stored;
                if (word.SequenceEqual("nested".AsSpan())) return TokenKind.Nested;
                if (word.SequenceEqual("public".AsSpan())) return TokenKind.Public;
                break;
            case 7:
                if (word.SequenceEqual("project".AsSpan())) return TokenKind.Project;
                if (word.SequenceEqual("extends".AsSpan())) return TokenKind.Extends;
                if (word.SequenceEqual("nothing".AsSpan())) return TokenKind.Nothing;
                if (word.SequenceEqual("private".AsSpan())) return TokenKind.Private;
                if (word.SequenceEqual("partial".AsSpan())) return TokenKind.Partial;
                if (word.SequenceEqual("methods".AsSpan())) return TokenKind.Methods;
                if (word.SequenceEqual("returns".AsSpan())) return TokenKind.Returns;
                if (word.SequenceEqual("injects".AsSpan())) return TokenKind.Injects;
                if (word.SequenceEqual("matches".AsSpan())) return TokenKind.Matches;
                if (word.SequenceEqual("require".AsSpan())) return TokenKind.Require;
                break;
            case 8:
                if (word.SequenceEqual("contract".AsSpan())) return TokenKind.Contract;
                if (word.SequenceEqual("abstract".AsSpan())) return TokenKind.Abstract;
                if (word.SequenceEqual("internal".AsSpan())) return TokenKind.Internal;
                if (word.SequenceEqual("readonly".AsSpan())) return TokenKind.Readonly;
                if (word.SequenceEqual("contains".AsSpan())) return TokenKind.Contains;
                if (word.SequenceEqual("inherits".AsSpan())) return TokenKind.Inherits;
                if (word.SequenceEqual("assigned".AsSpan())) return TokenKind.Assigned;
                break;
            case 9:
                if (word.SequenceEqual("protected".AsSpan())) return TokenKind.Protected;
                if (word.SequenceEqual("interface".AsSpan())) return TokenKind.Interface;
                if (word.SequenceEqual("namespace".AsSpan())) return TokenKind.Namespace;
                if (word.SequenceEqual("stateless".AsSpan())) return TokenKind.Stateless;
                if (word.SequenceEqual("parameter".AsSpan())) return TokenKind.Parameter;
                break;
            case 10:
                if (word.SequenceEqual("properties".AsSpan())) return TokenKind.Properties;
                if (word.SequenceEqual("attributes".AsSpan())) return TokenKind.Attributes;
                if (word.SequenceEqual("implements".AsSpan())) return TokenKind.Implements;
                break;
            case 12:
                if (word.SequenceEqual("require-each".AsSpan())) return TokenKind.RequireEach;
                if (word.SequenceEqual("null-checked".AsSpan())) return TokenKind.NullChecked;
                break;
        }

        return TokenKind.Identifier;
    }

    private static void Append(ref Token[] buffer, ref int count, Token token)
    {
        if (count == buffer.Length)
        {
            Token[] bigger = ArrayPool<Token>.Shared.Rent(buffer.Length * 2);
            Array.Copy(buffer, bigger, count);
            ArrayPool<Token>.Shared.Return(buffer);
            buffer = bigger;
        }

        buffer[count++] = token;
    }

    private static void Report(
        List<TangentDiagnostic> diagnostics, int fileIndex, string? filePath,
        int start, int length, int line, int column, string message)
    {
        diagnostics.Add(TangentDiagnostic.Create(
            DiagnosticCatalog.ParseError,
            new SourceSpan(fileIndex, start, length < 1 ? 1 : length, line, column),
            filePath,
            message));
    }
}
