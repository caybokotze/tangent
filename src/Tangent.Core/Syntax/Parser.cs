using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Tangent.Core.Diagnostics;
using Tangent.Core.Ir;
using Tangent.Core.Text;

namespace Tangent.Core.Syntax;

/// <summary>A hand-written recursive-descent parser for the Tangent DSL. It records diagnostics and never throws.</summary>
public sealed class Parser
{
    private enum Recovery : byte
    {
        TopLevel = 0,
        ProjectItem = 1,
        ContractItem = 2,
    }

    private readonly SourceFile _file;
    private readonly int _fileIndex;
    private readonly string _text;
    private readonly Token[] _tokens;
    private readonly StringTable _strings;
    private readonly List<TangentDiagnostic> _diagnostics;
    private readonly NodeList _nodes;
    private int _pos;
    private int _lastErrorToken = -1;

    private Parser(SourceFile file, int fileIndex, Token[] tokens, StringTable strings, List<TangentDiagnostic> diagnostics)
    {
        _file = file;
        _fileIndex = fileIndex;
        _text = file.Text;
        _tokens = tokens;
        _strings = strings;
        _diagnostics = diagnostics;
        _nodes = new NodeList(Math.Max(16, tokens.Length));
        _pos = 0;
    }

    /// <summary>Lexes and parses one file into a syntax tree.</summary>
    public static SyntaxTree Parse(SourceFile file, int fileIndex, StringTable strings)
    {
        if (strings is null) throw new ArgumentNullException(nameof(strings));
        var diagnostics = new List<TangentDiagnostic>();
        Token[] tokens = Lexer.Lex(file.Span, fileIndex, file.Path, diagnostics);
        var parser = new Parser(file, fileIndex, tokens, strings, diagnostics);
        int root = parser.ParseFile();
        return new SyntaxTree(file, fileIndex, tokens, parser._nodes, root, strings, diagnostics.ToImmutableArray());
    }

    /// <summary>Lexes and parses one file with a fresh string table.</summary>
    public static SyntaxTree Parse(SourceFile file) => Parse(file, 0, new StringTable());

    // ------------------------------------------------------------------
    // token helpers
    // ------------------------------------------------------------------

    private TokenKind Kind => _tokens[_pos].Kind;

    private Token Current => _tokens[_pos];

    private TokenKind PeekKind(int offset)
    {
        int index = _pos + offset;
        if (index >= _tokens.Length) index = _tokens.Length - 1;
        return _tokens[index].Kind;
    }

    private Token Advance()
    {
        Token token = _tokens[_pos];
        if (token.Kind != TokenKind.EndOfFile) _pos++;
        return token;
    }

    private bool Match(TokenKind kind)
    {
        if (Kind != kind) return false;
        Advance();
        return true;
    }

    private bool Expect(TokenKind kind, string message)
    {
        if (Kind == kind)
        {
            Advance();
            return true;
        }

        Error(message);
        return false;
    }

    private SourceSpan SpanOf(Token token) =>
        new SourceSpan(_fileIndex, token.Start, token.Length, token.Line, token.Column);

    private SourceSpan SpanFrom(Token start) => SpanFromTo(start, _pos);

    /// <summary>The span from <paramref name="start"/> up to the token before <paramref name="endPos"/>.</summary>
    private SourceSpan SpanFromTo(Token start, int endPos)
    {
        int previous = endPos > 0 ? endPos - 1 : 0;
        Token last = _tokens[previous];
        int end = last.End;
        int begin = start.Start;
        if (end < begin + start.Length) end = begin + start.Length;
        return new SourceSpan(_fileIndex, begin, end - begin, start.Line, start.Column);
    }

    private StringId Intern(Token token) => _strings.Intern(_text.AsSpan(token.Start, token.Length));

    private StringId InternString(Token token)
    {
        ReadOnlySpan<char> content = Lexer.StringContent(_text.AsSpan(token.Start, token.Length));
        return Lexer.NeedsUnescaping(content)
            ? _strings.Intern(Lexer.Unescape(content))
            : _strings.Intern(content);
    }

    private void Error(string message)
    {
        if (_lastErrorToken == _pos) return;
        _lastErrorToken = _pos;
        Token token = Current;
        _diagnostics.Add(TangentDiagnostic.Create(
            DiagnosticCatalog.ParseError,
            SpanOf(token),
            _file.Path,
            message + ", found " + DescribeCurrent()));
    }

    private string DescribeCurrent()
    {
        Token token = Current;
        switch (token.Kind)
        {
            case TokenKind.EndOfFile:
                return "end of file";
            case TokenKind.Identifier:
            case TokenKind.Glob:
            case TokenKind.Template:
            case TokenKind.StringLiteral:
            case TokenKind.Unknown:
                return "'" + _text.Substring(token.Start, token.Length) + "'";
            default:
                return TokenKinds.Describe(token.Kind);
        }
    }

    private bool IsRecoveryPoint(Recovery context, TokenKind kind)
    {
        switch (kind)
        {
            case TokenKind.Project:
            case TokenKind.Contract:
            case TokenKind.Bind:
            case TokenKind.Exempt:
                return true;
        }

        if (context == Recovery.ProjectItem)
        {
            return kind == TokenKind.Root || kind == TokenKind.Layer || kind == TokenKind.Allow;
        }

        if (context == Recovery.ContractItem)
        {
            if (kind == TokenKind.Match || kind == TokenKind.Require || kind == TokenKind.RequireEach ||
                kind == TokenKind.Forbid || kind == TokenKind.Where)
            {
                return true;
            }

            return StartsPredicate(kind);
        }

        return false;
    }

    /// <summary>Consumes the offending token then skips to the next recovery point or to a <c>}</c> at the current depth.</summary>
    private void Synchronize(Recovery context)
    {
        int depth = 0;
        bool consumed = false;
        while (true)
        {
            TokenKind kind = Kind;
            if (kind == TokenKind.EndOfFile) return;
            if (depth == 0 && kind == TokenKind.RightBrace) return;
            if (depth == 0 && consumed && IsRecoveryPoint(context, kind)) return;

            if (kind == TokenKind.LeftBrace) depth++;
            else if (kind == TokenKind.RightBrace) depth--;

            _pos++;
            consumed = true;
        }
    }

    private void ExpectCloseBrace(string what)
    {
        if (Kind == TokenKind.RightBrace)
        {
            Advance();
            return;
        }

        Error("expected '}' to close " + what);
    }

    // ------------------------------------------------------------------
    // declarations
    // ------------------------------------------------------------------

    private int ParseFile()
    {
        int file = _nodes.Add(NodeKind.File, new SourceSpan(_fileIndex, 0, _text.Length, 1, 1));
        while (Kind != TokenKind.EndOfFile)
        {
            int before = _pos;
            switch (Kind)
            {
                case TokenKind.Semicolon:
                    Advance();
                    break;
                case TokenKind.Project:
                    ParseProject(file);
                    break;
                case TokenKind.Contract:
                    ParseContract(file);
                    break;
                case TokenKind.Bind:
                    ParseBind(file);
                    break;
                case TokenKind.Exempt:
                    ParseExempt(file);
                    break;
                default:
                    Error("expected 'project', 'contract', 'bind' or 'exempt'");
                    Synchronize(Recovery.TopLevel);
                    break;
            }

            // Guarantee progress: a stray '}' at file scope has no block to close.
            if (_pos == before) Advance();
        }

        return file;
    }

    private void ParseProject(int parent)
    {
        Token keyword = Advance();
        StringId name = StringId.Invalid;
        if (Kind == TokenKind.StringLiteral) name = InternString(Advance());
        else Error("expected a project name string after 'project'");

        int node = _nodes.Add(NodeKind.Project, SpanOf(keyword), name.Value);
        _nodes.AddChild(parent, node);

        if (!Expect(TokenKind.LeftBrace, "expected '{' after the project name"))
        {
            Synchronize(Recovery.TopLevel);
            _nodes.SetSpan(node, SpanFrom(keyword));
            return;
        }

        while (Kind != TokenKind.RightBrace && Kind != TokenKind.EndOfFile)
        {
            ParseProjectItem(node);
        }

        ExpectCloseBrace("the 'project' block");
        _nodes.SetSpan(node, SpanFrom(keyword));
    }

    private void ParseProjectItem(int parent)
    {
        switch (Kind)
        {
            case TokenKind.Semicolon:
                Advance();
                return;

            case TokenKind.Root:
            {
                Token keyword = Advance();
                Expect(TokenKind.Colon, "expected ':' after 'root'");
                StringId path = StringId.Invalid;
                if (Kind == TokenKind.StringLiteral) path = InternString(Advance());
                else Error("expected a path string after 'root:'");
                _nodes.AddChild(parent, _nodes.Add(NodeKind.ProjectRoot, SpanFrom(keyword), path.Value));
                return;
            }

            case TokenKind.Layer:
            {
                Token keyword = Advance();
                StringId name = StringId.Invalid;
                if (Kind == TokenKind.Identifier) name = Intern(Advance());
                else Error("expected a layer name after 'layer'");

                Expect(TokenKind.Equals, "expected '=' after the layer name");

                StringId glob = StringId.Invalid;
                if (Kind == TokenKind.Glob || Kind == TokenKind.Identifier) glob = Intern(Advance());
                else Error("expected a namespace glob after '='");

                _nodes.AddChild(parent, _nodes.Add(NodeKind.LayerDecl, SpanFrom(keyword), name.Value, glob.Value));
                return;
            }

            case TokenKind.Allow:
            {
                Token keyword = Advance();
                int node = _nodes.Add(NodeKind.Allow, SpanOf(keyword));
                ParseGlobList(node);
                _nodes.SetSpan(node, SpanFrom(keyword));
                _nodes.AddChild(parent, node);
                return;
            }

            case TokenKind.Identifier:
            {
                Token from = Advance();
                int node = _nodes.Add(NodeKind.Dependency, SpanOf(from), Intern(from).Value);
                _nodes.AddChild(parent, node);
                if (!Expect(TokenKind.Arrow, "expected '->' after the layer name"))
                {
                    Synchronize(Recovery.ProjectItem);
                    return;
                }

                if (Kind == TokenKind.Nothing)
                {
                    Token nothing = Advance();
                    _nodes.AddChild(node, _nodes.Add(NodeKind.Nothing, SpanOf(nothing)));
                }
                else
                {
                    do
                    {
                        if (Kind == TokenKind.Identifier)
                        {
                            Token target = Advance();
                            _nodes.AddChild(node, _nodes.Add(NodeKind.LayerRef, SpanOf(target), Intern(target).Value));
                        }
                        else
                        {
                            Error("expected a layer name or 'nothing' after '->'");
                            break;
                        }
                    }
                    while (Match(TokenKind.Comma));
                }

                _nodes.SetSpan(node, SpanFrom(from));
                return;
            }

            default:
                Error("expected 'root', 'layer', 'allow' or a layer dependency line");
                Synchronize(Recovery.ProjectItem);
                return;
        }
    }

    private void ParseContract(int parent)
    {
        Token keyword = Advance();
        StringId name = StringId.Invalid;
        if (Kind == TokenKind.Identifier) name = Intern(Advance());
        else Error("expected a contract name after 'contract'");

        StringId baseName = StringId.Invalid;
        if (Match(TokenKind.Extends))
        {
            if (Kind == TokenKind.Identifier) baseName = Intern(Advance());
            else Error("expected a contract name after 'extends'");
        }

        int node = _nodes.Add(NodeKind.Contract, SpanOf(keyword), name.Value, baseName.Value);
        _nodes.AddChild(parent, node);

        if (!Expect(TokenKind.LeftBrace, "expected '{' after the contract name"))
        {
            Synchronize(Recovery.TopLevel);
            _nodes.SetSpan(node, SpanFrom(keyword));
            return;
        }

        while (Kind != TokenKind.RightBrace && Kind != TokenKind.EndOfFile)
        {
            ParseContractItem(node, allowMemberBlocks: true);
        }

        ExpectCloseBrace("the 'contract' block");
        _nodes.SetSpan(node, SpanFrom(keyword));
    }

    private void ParseContractItem(int parent, bool allowMemberBlocks)
    {
        switch (Kind)
        {
            case TokenKind.Semicolon:
                Advance();
                return;

            case TokenKind.Match:
                if (allowMemberBlocks)
                {
                    ParseMatch(parent);
                    return;
                }

                break;

            case TokenKind.Where:
                ParseRefinement(parent);
                return;

            case TokenKind.Require:
            case TokenKind.RequireEach:
            case TokenKind.Forbid:
                ParseRule(parent);
                return;
        }

        if (allowMemberBlocks && TokenKinds.IsMemberKind(Kind) &&
            (PeekKind(1) == TokenKind.LeftBrace || PeekKind(1) == TokenKind.Where))
        {
            ParseMemberBlock(parent);
            return;
        }

        if (StartsPredicate(Kind))
        {
            ParseRule(parent);
            return;
        }

        Error(allowMemberBlocks
            ? "expected 'match', a rule, or a member block such as 'ctor { ... }'"
            : "expected a rule or a 'where' refinement");
        Synchronize(Recovery.ContractItem);
    }

    private void ParseMatch(int parent)
    {
        Token keyword = Advance();
        Expect(TokenKind.Colon, "expected ':' after 'match'");

        TypeKind kind = TypeKind.Any;
        switch (Kind)
        {
            case TokenKind.Class: kind = TypeKind.Class; Advance(); break;
            case TokenKind.Interface: kind = TypeKind.Interface; Advance(); break;
            case TokenKind.Record: kind = TypeKind.Record; Advance(); break;
            case TokenKind.Struct: kind = TypeKind.Struct; Advance(); break;
            case TokenKind.Enum: kind = TypeKind.Enum; Advance(); break;
            case TokenKind.Any: kind = TypeKind.Any; Advance(); break;
            default:
                Error("expected 'class', 'interface', 'record', 'struct', 'enum' or 'any' after 'match:'");
                break;
        }

        int node = _nodes.Add(NodeKind.Match, SpanOf(keyword), (int)kind);
        _nodes.AddChild(parent, node);

        if (!Expect(TokenKind.In, "expected 'in' after the match kind"))
        {
            Synchronize(Recovery.ContractItem);
            _nodes.SetSpan(node, SpanFrom(keyword));
            return;
        }

        if (Kind == TokenKind.Glob)
        {
            Token glob = Advance();
            _nodes.AddChild(node, _nodes.Add(NodeKind.GlobRef, SpanOf(glob), Intern(glob).Value));
        }
        else if (Kind == TokenKind.Identifier)
        {
            Token layer = Advance();
            _nodes.AddChild(node, _nodes.Add(NodeKind.LayerRef, SpanOf(layer), Intern(layer).Value));
        }
        else
        {
            Error("expected a layer name or a namespace glob after 'in'");
        }

        // A `where` here is ambiguous: it is either this match clause's own condition
        // (`match: class in Api where name ends with "X"`) or the start of a top-level refinement that
        // follows the match line (`match: class in Api` / `where name ends with "X" { sealed }`).
        // Only a refinement's condition is followed by `{`, so parse it and then look.
        if (Kind == TokenKind.Where)
        {
            int wherePos = _pos;
            Token where = Advance();
            int condition = ParseCondition();

            if (Kind == TokenKind.LeftBrace)
            {
                _nodes.SetSpan(node, SpanFromTo(keyword, wherePos));
                ParseRefinementBody(parent, where, condition);
                return;
            }

            if (condition >= 0) _nodes.AddChild(node, condition);
        }

        _nodes.SetSpan(node, SpanFrom(keyword));
    }

    private void ParseMemberBlock(int parent)
    {
        Token keyword = Advance();
        MemberKind memberKind = ToMemberKind(keyword.Kind);
        int node = _nodes.Add(NodeKind.MemberBlock, SpanOf(keyword), (int)memberKind);
        _nodes.AddChild(parent, node);

        if (Match(TokenKind.Where))
        {
            int condition = ParseCondition();
            if (condition >= 0) _nodes.AddChild(node, condition);
        }

        string what = "'" + (TokenKinds.KeywordText(keyword.Kind) ?? "member block") + "'";
        if (!Expect(TokenKind.LeftBrace, "expected '{' after " + what))
        {
            Synchronize(Recovery.ContractItem);
            _nodes.SetSpan(node, SpanFrom(keyword));
            return;
        }

        while (Kind != TokenKind.RightBrace && Kind != TokenKind.EndOfFile)
        {
            ParseContractItem(node, allowMemberBlocks: false);
        }

        ExpectCloseBrace("the " + what + " block");
        _nodes.SetSpan(node, SpanFrom(keyword));
    }

    private void ParseRefinement(int parent)
    {
        Token keyword = Advance();
        int condition = ParseCondition();
        ParseRefinementBody(parent, keyword, condition);
    }

    /// <summary>Parses the <c>{ ... }</c> of a refinement whose <c>where</c> and condition are already consumed.</summary>
    private void ParseRefinementBody(int parent, Token keyword, int condition)
    {
        int node = _nodes.Add(NodeKind.Refinement, SpanOf(keyword));
        _nodes.AddChild(parent, node);
        if (condition >= 0) _nodes.AddChild(node, condition);

        if (!Expect(TokenKind.LeftBrace, "expected '{' after a 'where' condition"))
        {
            Synchronize(Recovery.ContractItem);
            _nodes.SetSpan(node, SpanFrom(keyword));
            return;
        }

        while (Kind != TokenKind.RightBrace && Kind != TokenKind.EndOfFile)
        {
            ParseContractItem(node, allowMemberBlocks: false);
        }

        ExpectCloseBrace("the 'where' block");
        _nodes.SetSpan(node, SpanFrom(keyword));
    }

    private void ParseRule(int parent)
    {
        Token start = Current;
        Polarity polarity = Polarity.Require;
        if (Kind == TokenKind.Require || Kind == TokenKind.RequireEach)
        {
            Advance();
        }
        else if (Kind == TokenKind.Forbid)
        {
            polarity = Polarity.Forbid;
            Advance();
        }

        int node = _nodes.Add(NodeKind.Rule, SpanOf(start), (int)polarity);
        _nodes.AddChild(parent, node);

        int predicate = ParsePredicate();
        if (predicate < 0)
        {
            Synchronize(Recovery.ContractItem);
            _nodes.SetSpan(node, SpanFrom(start));
            return;
        }

        _nodes.AddChild(node, predicate);
        _nodes.SetSpan(node, SpanFrom(start));
    }

    // ------------------------------------------------------------------
    // predicates
    // ------------------------------------------------------------------

    private static bool StartsPredicate(TokenKind kind)
    {
        switch (kind)
        {
            case TokenKind.Inherits:
            case TokenKind.Implements:
            case TokenKind.Attributes:
            case TokenKind.Returns:
            case TokenKind.Injects:
            case TokenKind.Using:
            case TokenKind.New:
            case TokenKind.Get:
            case TokenKind.Set:
            case TokenKind.Init:
            case TokenKind.Each:
            case TokenKind.Parameter:
            case TokenKind.Stateless:
                return true;
            default:
                return TokenKinds.IsModifier(kind) || TokenKinds.IsMemberKind(kind);
        }
    }

    private int ParsePredicate()
    {
        Token start = Current;
        switch (Kind)
        {
            case TokenKind.Inherits:
            {
                Advance();
                int node = _nodes.Add(NodeKind.PredInherits, SpanOf(start));
                ParseTypeAltList(node, allowNewPrefix: false);
                _nodes.SetSpan(node, SpanFrom(start));
                return node;
            }

            case TokenKind.Implements:
            {
                Advance();
                int node = _nodes.Add(NodeKind.PredImplements, SpanOf(start));
                ParseTypeAltList(node, allowNewPrefix: false);
                _nodes.SetSpan(node, SpanFrom(start));
                return node;
            }

            case TokenKind.Returns:
            {
                Advance();
                int node = _nodes.Add(NodeKind.PredReturns, SpanOf(start));
                ParseTypeAltList(node, allowNewPrefix: false);
                _nodes.SetSpan(node, SpanFrom(start));
                return node;
            }

            case TokenKind.Attributes:
            {
                Advance();
                int node = _nodes.Add(NodeKind.PredAttributes, SpanOf(start));
                ParseAttributeList(node);
                _nodes.SetSpan(node, SpanFrom(start));
                return node;
            }

            case TokenKind.Injects:
            {
                Advance();
                bool only = Match(TokenKind.Only);
                int node = _nodes.Add(only ? NodeKind.PredInjectsOnly : NodeKind.PredInjects, SpanOf(start));
                if (only)
                {
                    int group = ParseTypeAltSingleGroup(allowCommas: true);
                    if (group >= 0) _nodes.AddChild(node, group);
                }
                else
                {
                    ParseTypeAltList(node, allowNewPrefix: false);
                }

                _nodes.SetSpan(node, SpanFrom(start));
                return node;
            }

            case TokenKind.Using:
            {
                Advance();
                int node = _nodes.Add(NodeKind.PredUsing, SpanOf(start));
                ParseGlobList(node);
                _nodes.SetSpan(node, SpanFrom(start));
                return node;
            }

            case TokenKind.New:
            {
                Advance();
                int node = _nodes.Add(NodeKind.PredNew, SpanOf(start));
                ParseTypeAltList(node, allowNewPrefix: true);
                _nodes.SetSpan(node, SpanFrom(start));
                return node;
            }

            case TokenKind.Get:
            case TokenKind.Set:
            case TokenKind.Init:
            {
                Token accessor = Advance();
                return _nodes.Add(NodeKind.PredAccessor, SpanOf(accessor), (int)ToAccessorKind(accessor.Kind), (int)Modifiers.None);
            }

            case TokenKind.Each:
            {
                Advance();
                if (!Expect(TokenKind.Parameter, "expected 'parameter' after 'each'")) return -1;
                return ParseEachParameterTail(start);
            }

            case TokenKind.Parameter:
            {
                Advance();
                return ParseEachParameterTail(start);
            }

            case TokenKind.Stateless:
            {
                Token keyword = Advance();
                return _nodes.Add(NodeKind.PredStateless, SpanOf(keyword));
            }

            case TokenKind.Interface:
                if ((PeekKind(1) == TokenKind.Template || PeekKind(1) == TokenKind.Identifier) && PeekKind(2) == TokenKind.Lives)
                {
                    Advance();
                    Token template = Advance();
                    StringId templateText = Intern(template);
                    Advance(); // lives
                    Expect(TokenKind.In, "expected 'in' after 'lives'");
                    StringId layer = StringId.Invalid;
                    if (Kind == TokenKind.Identifier) layer = Intern(Advance());
                    else Error("expected a layer name after 'lives in'");
                    return _nodes.Add(NodeKind.PredInterfaceLivesIn, SpanFrom(start), templateText.Value, layer.Value);
                }

                break;
        }

        if (TokenKinds.IsModifier(Kind))
        {
            Modifiers modifiers = Modifiers.None;
            while (TokenKinds.IsModifier(Kind))
            {
                modifiers |= ToModifier(Kind);
                Advance();
            }

            // `sealed` followed by `ctor { ... }` is a modifier rule and then a member block,
            // not a "sealed ctor" member-kind predicate.
            if (TokenKinds.IsMemberKind(Kind) && PeekKind(1) != TokenKind.LeftBrace && PeekKind(1) != TokenKind.Where)
            {
                Token member = Advance();
                return _nodes.Add(NodeKind.PredMemberKind, SpanFrom(start), (int)ToMemberKind(member.Kind), (int)modifiers);
            }

            if (Kind == TokenKind.Get || Kind == TokenKind.Set || Kind == TokenKind.Init)
            {
                Token accessor = Advance();
                return _nodes.Add(NodeKind.PredAccessor, SpanFrom(start), (int)ToAccessorKind(accessor.Kind), (int)modifiers);
            }

            return _nodes.Add(NodeKind.PredModifier, SpanFrom(start), (int)modifiers);
        }

        if (TokenKinds.IsMemberKind(Kind))
        {
            Token member = Advance();
            return _nodes.Add(NodeKind.PredMemberKind, SpanOf(member), (int)ToMemberKind(member.Kind), (int)Modifiers.None);
        }

        Error("expected a predicate such as 'sealed', 'inherits T', 'attributes [A]' or 'using Ns.**'");
        return -1;
    }

    private int ParseEachParameterTail(Token start)
    {
        if (Match(TokenKind.Assigned))
        {
            Expect(TokenKind.To, "expected 'to' after 'assigned'");
            Modifiers modifiers = Modifiers.None;
            while (TokenKinds.IsModifier(Kind))
            {
                modifiers |= ToModifier(Kind);
                Advance();
            }

            Expect(TokenKind.Field, "expected 'field' after 'assigned to'");
            return _nodes.Add(NodeKind.PredEachParamAssignedToField, SpanFrom(start), (int)modifiers);
        }

        if (Match(TokenKind.NullChecked))
        {
            return _nodes.Add(NodeKind.PredEachParamNullChecked, SpanFrom(start));
        }

        if (Match(TokenKind.Not))
        {
            Expect(TokenKind.Stored, "expected 'stored' after 'not'");
            return _nodes.Add(NodeKind.PredEachParamNotStored, SpanFrom(start));
        }

        Error("expected 'assigned to field', 'null-checked' or 'not stored' after 'each parameter'");
        return -1;
    }

    // ------------------------------------------------------------------
    // type patterns, attributes, globs
    // ------------------------------------------------------------------

    private void ParseTypeAltList(int parent, bool allowNewPrefix)
    {
        do
        {
            Token start = Current;
            int alt = _nodes.Add(NodeKind.TypeAlt, SpanOf(start));
            bool any = false;
            do
            {
                if (allowNewPrefix) Match(TokenKind.New);
                int pattern = ParseTypePattern();
                if (pattern < 0) break;
                _nodes.AddChild(alt, pattern);
                any = true;
            }
            while (Match(TokenKind.Pipe));

            _nodes.SetSpan(alt, SpanFrom(start));
            _nodes.AddChild(parent, alt);
            if (!any) return;
        }
        while (Match(TokenKind.Comma));
    }

    private int ParseTypeAltSingleGroup(bool allowCommas)
    {
        Token start = Current;
        int alt = _nodes.Add(NodeKind.TypeAlt, SpanOf(start));
        while (true)
        {
            int pattern = ParseTypePattern();
            if (pattern < 0) break;
            _nodes.AddChild(alt, pattern);
            if (Match(TokenKind.Pipe)) continue;
            if (allowCommas && Match(TokenKind.Comma)) continue;
            break;
        }

        _nodes.SetSpan(alt, SpanFrom(start));
        return alt;
    }

    private int ParseTypePattern()
    {
        Token start = Current;
        int node;
        if (Kind == TokenKind.Star)
        {
            Advance();
            return _nodes.Add(NodeKind.TypeWildcard, SpanOf(start));
        }

        if (Kind == TokenKind.Template)
        {
            Advance();
            node = _nodes.Add(NodeKind.TypeTemplate, SpanOf(start), Intern(start).Value);
        }
        else if (Kind == TokenKind.Identifier)
        {
            Advance();
            node = _nodes.Add(NodeKind.TypePattern, SpanOf(start), Intern(start).Value);
        }
        else
        {
            Error("expected a type name");
            return -1;
        }

        if (Match(TokenKind.LessThan))
        {
            do
            {
                int argument = ParseTypePattern();
                if (argument < 0) break;
                _nodes.AddChild(node, argument);
            }
            while (Match(TokenKind.Comma));

            Expect(TokenKind.GreaterThan, "expected '>' to close the type arguments");
        }

        _nodes.SetSpan(node, SpanFrom(start));
        return node;
    }

    private void ParseAttributeList(int parent)
    {
        do
        {
            Token start = Current;
            if (!Expect(TokenKind.LeftBracket, "expected '[' to start an attribute")) return;

            int group = _nodes.Add(NodeKind.AttrGroup, SpanOf(start));
            do
            {
                if (Kind != TokenKind.Identifier)
                {
                    Error("expected an attribute name");
                    break;
                }

                Token name = Advance();
                int anyArgs = 0;
                if (Match(TokenKind.LeftParen))
                {
                    if (Match(TokenKind.Star)) anyArgs = 1;
                    else Error("expected '*' inside the attribute argument list");
                    Expect(TokenKind.RightParen, "expected ')' to close the attribute arguments");
                }

                _nodes.AddChild(group, _nodes.Add(NodeKind.AttrRef, SpanFrom(name), Intern(name).Value, anyArgs));
            }
            while (Match(TokenKind.Pipe));

            Expect(TokenKind.RightBracket, "expected ']' to close the attribute");
            _nodes.SetSpan(group, SpanFrom(start));
            _nodes.AddChild(parent, group);
        }
        while (Match(TokenKind.Comma));
    }

    private void ParseGlobList(int parent)
    {
        do
        {
            if (Kind == TokenKind.Glob || Kind == TokenKind.Identifier)
            {
                Token glob = Advance();
                _nodes.AddChild(parent, _nodes.Add(NodeKind.GlobRef, SpanOf(glob), Intern(glob).Value));
            }
            else
            {
                Error("expected a namespace glob such as 'Orders.Infrastructure.**'");
                return;
            }
        }
        while (Match(TokenKind.Comma));
    }

    // ------------------------------------------------------------------
    // conditions
    // ------------------------------------------------------------------

    private int ParseCondition() => ParseOrCondition();

    private int ParseOrCondition()
    {
        int left = ParseAndCondition();
        while (Kind == TokenKind.Or)
        {
            Token keyword = Advance();
            int right = ParseAndCondition();
            if (left < 0 || right < 0) return left;
            int node = _nodes.Add(NodeKind.CondOr, SpanOf(keyword));
            _nodes.AddChild(node, left);
            _nodes.AddChild(node, right);
            left = node;
        }

        return left;
    }

    private int ParseAndCondition()
    {
        int left = ParseSimpleCondition();
        while (Kind == TokenKind.And)
        {
            Token keyword = Advance();
            int right = ParseSimpleCondition();
            if (left < 0 || right < 0) return left;
            int node = _nodes.Add(NodeKind.CondAnd, SpanOf(keyword));
            _nodes.AddChild(node, left);
            _nodes.AddChild(node, right);
            left = node;
        }

        return left;
    }

    private int ParseSimpleCondition()
    {
        Token start = Current;
        switch (Kind)
        {
            case TokenKind.Name:
            {
                Advance();
                NodeKind kind;
                if (Match(TokenKind.Starts))
                {
                    Expect(TokenKind.With, "expected 'with' after 'starts'");
                    kind = NodeKind.CondNameStartsWith;
                }
                else if (Match(TokenKind.Ends))
                {
                    Expect(TokenKind.With, "expected 'with' after 'ends'");
                    kind = NodeKind.CondNameEndsWith;
                }
                else if (Match(TokenKind.Contains))
                {
                    kind = NodeKind.CondNameContains;
                }
                else if (Match(TokenKind.Matches))
                {
                    kind = NodeKind.CondNameMatches;
                }
                else
                {
                    Error("expected 'starts with', 'ends with', 'contains' or 'matches' after 'name'");
                    return -1;
                }

                StringId value = StringId.Invalid;
                if (Kind == TokenKind.StringLiteral) value = InternString(Advance());
                else Error("expected a string");

                return _nodes.Add(kind, SpanFrom(start), value.Value);
            }

            case TokenKind.Inherits:
            {
                Advance();
                int node = _nodes.Add(NodeKind.CondInherits, SpanOf(start));
                int group = ParseTypeAltSingleGroup(allowCommas: false);
                if (group >= 0) _nodes.AddChild(node, group);
                _nodes.SetSpan(node, SpanFrom(start));
                return node;
            }

            case TokenKind.Implements:
            {
                Advance();
                int node = _nodes.Add(NodeKind.CondImplements, SpanOf(start));
                int group = ParseTypeAltSingleGroup(allowCommas: false);
                if (group >= 0) _nodes.AddChild(node, group);
                _nodes.SetSpan(node, SpanFrom(start));
                return node;
            }

            case TokenKind.Attributes:
            {
                Advance();
                int node = _nodes.Add(NodeKind.CondAttributes, SpanOf(start));
                ParseAttributeList(node);
                _nodes.SetSpan(node, SpanFrom(start));
                return node;
            }
        }

        if (TokenKinds.IsModifier(Kind))
        {
            Modifiers modifiers = Modifiers.None;
            while (TokenKinds.IsModifier(Kind))
            {
                modifiers |= ToModifier(Kind);
                Advance();
            }

            return _nodes.Add(NodeKind.CondModifier, SpanFrom(start), (int)modifiers);
        }

        Error("expected a condition such as 'name ends with \"X\"', 'inherits T' or a modifier");
        return -1;
    }

    // ------------------------------------------------------------------
    // bind / exempt
    // ------------------------------------------------------------------

    private void ParseBind(int parent)
    {
        Token keyword = Advance();
        StringId typeName = StringId.Invalid;
        if (Kind == TokenKind.Identifier) typeName = Intern(Advance());
        else Error("expected a fully qualified type name after 'bind'");

        int node = _nodes.Add(NodeKind.Bind, SpanOf(keyword), typeName.Value);
        _nodes.AddChild(parent, node);

        if (!Expect(TokenKind.To, "expected 'to' after the type name in a 'bind' statement"))
        {
            Synchronize(Recovery.TopLevel);
            _nodes.SetSpan(node, SpanFrom(keyword));
            return;
        }

        do
        {
            if (Kind == TokenKind.Identifier)
            {
                Token contract = Advance();
                _nodes.AddChild(node, _nodes.Add(NodeKind.ContractRef, SpanOf(contract), Intern(contract).Value));
            }
            else
            {
                Error("expected a contract name");
                break;
            }
        }
        while (Match(TokenKind.Comma));

        if (Match(TokenKind.Except))
        {
            do
            {
                if (Kind == TokenKind.LeftBracket)
                {
                    Token bracket = Advance();
                    if (Kind == TokenKind.Identifier)
                    {
                        Token name = Advance();
                        _nodes.AddChild(node, _nodes.Add(NodeKind.ExceptAttribute, SpanFrom(bracket), Intern(name).Value));
                    }
                    else
                    {
                        Error("expected an attribute name");
                    }

                    Expect(TokenKind.RightBracket, "expected ']' to close the attribute");
                }
                else if (Kind == TokenKind.StringLiteral)
                {
                    Token rule = Advance();
                    _nodes.AddChild(node, _nodes.Add(NodeKind.ExceptRule, SpanOf(rule), InternString(rule).Value));
                }
                else
                {
                    Error("expected an attribute like '[Name]' or a rule id string after 'except'");
                    break;
                }
            }
            while (Match(TokenKind.Comma));
        }

        _nodes.SetSpan(node, SpanFrom(keyword));
    }

    private void ParseExempt(int parent)
    {
        Token keyword = Advance();
        StringId typeName = StringId.Invalid;
        if (Kind == TokenKind.Identifier) typeName = Intern(Advance());
        else Error("expected a fully qualified type name after 'exempt'");

        int node = _nodes.Add(NodeKind.Exempt, SpanOf(keyword), typeName.Value, StringId.Invalid.Value);
        _nodes.AddChild(parent, node);

        if (Match(TokenKind.From))
        {
            do
            {
                if (Kind == TokenKind.Identifier)
                {
                    Token contract = Advance();
                    _nodes.AddChild(node, _nodes.Add(NodeKind.ContractRef, SpanOf(contract), Intern(contract).Value));
                }
                else
                {
                    Error("expected a contract name after 'from'");
                    break;
                }
            }
            while (Match(TokenKind.Comma));
        }

        StringId reason = StringId.Invalid;
        if (Expect(TokenKind.Reason, "expected 'reason' in an 'exempt' statement"))
        {
            if (Kind == TokenKind.StringLiteral) reason = InternString(Advance());
            else Error("expected a reason string after 'reason'");
        }
        else
        {
            Synchronize(Recovery.TopLevel);
        }

        _nodes.SetPayloads(node, typeName.Value, reason.Value);
        _nodes.SetSpan(node, SpanFrom(keyword));
    }

    // ------------------------------------------------------------------
    // mapping helpers
    // ------------------------------------------------------------------

    private static MemberKind ToMemberKind(TokenKind kind)
    {
        switch (kind)
        {
            case TokenKind.Ctor: return MemberKind.Ctor;
            case TokenKind.Methods: return MemberKind.Method;
            case TokenKind.Properties: return MemberKind.Property;
            case TokenKind.Fields: return MemberKind.Field;
            case TokenKind.Events: return MemberKind.Event;
            case TokenKind.Nested: return MemberKind.Nested;
            default: return MemberKind.Type;
        }
    }

    private static AccessorKind ToAccessorKind(TokenKind kind)
    {
        switch (kind)
        {
            case TokenKind.Set: return AccessorKind.Set;
            case TokenKind.Init: return AccessorKind.Init;
            default: return AccessorKind.Get;
        }
    }

    private static Modifiers ToModifier(TokenKind kind)
    {
        switch (kind)
        {
            case TokenKind.Public: return Modifiers.Public;
            case TokenKind.Private: return Modifiers.Private;
            case TokenKind.Protected: return Modifiers.Protected;
            case TokenKind.Internal: return Modifiers.Internal;
            case TokenKind.Static: return Modifiers.Static;
            case TokenKind.Sealed: return Modifiers.Sealed;
            case TokenKind.Abstract: return Modifiers.Abstract;
            case TokenKind.Readonly: return Modifiers.Readonly;
            case TokenKind.Partial: return Modifiers.Partial;
            case TokenKind.Class: return Modifiers.Class;
            case TokenKind.Record: return Modifiers.Record;
            case TokenKind.Struct: return Modifiers.Struct;
            case TokenKind.Interface: return Modifiers.Interface;
            default: return Modifiers.None;
        }
    }
}
