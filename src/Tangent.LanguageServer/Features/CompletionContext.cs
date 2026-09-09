using Tangent.Core.Syntax;
using Tangent.LanguageServer.Workspace;

namespace Tangent.LanguageServer.Features;

/// <summary>What the cursor is in the middle of writing.</summary>
internal enum CompletionContextKind
{
    /// <summary>Outside any block: <c>project</c>, <c>contract</c>, <c>bind</c>, <c>exempt</c>.</summary>
    TopLevel,

    /// <summary>Inside a <c>project</c> block.</summary>
    ProjectBody,

    /// <summary>Inside a contract, member block or refinement.</summary>
    ContractBody,

    /// <summary>A declared layer name is expected.</summary>
    Layers,

    /// <summary>A contract name is expected.</summary>
    Contracts,

    /// <summary>A C# type name is expected.</summary>
    TypeNames,

    /// <summary>A namespace glob is expected.</summary>
    Namespaces,

    /// <summary>An attribute name is expected.</summary>
    Attributes,
}

/// <summary>The analysed completion position.</summary>
/// <param name="Kind">What is expected here.</param>
/// <param name="Prefix">The partially typed word, possibly empty.</param>
/// <param name="PrefixStart">The offset the partially typed word starts at.</param>
internal readonly record struct TanCompletionContext(CompletionContextKind Kind, string Prefix, int PrefixStart);

/// <summary>Decides what a completion request at an offset should offer, from the token stream alone.</summary>
internal static class CompletionContextAnalyzer
{
    private enum Block
    {
        Unknown,
        Project,
        Contract,
        Member,
    }

    /// <summary>Analyses the cursor position.</summary>
    public static TanCompletionContext Analyze(TanDocument document, int offset)
    {
        Token[] tokens = document.Tree.Tokens;
        string text = document.Text;

        int wordIndex = -1;
        string prefix = string.Empty;
        int prefixStart = offset;
        int last = -1;

        for (int i = 0; i < tokens.Length; i++)
        {
            Token token = tokens[i];
            if (token.Kind == TokenKind.EndOfFile) break;
            if (token.End > offset) break;
            last = i;
        }

        if (last >= 0 && tokens[last].End == offset && IsWordLike(tokens[last].Kind))
        {
            wordIndex = last;
            prefix = text.Substring(tokens[last].Start, tokens[last].Length);
            prefixStart = tokens[last].Start;
            last--;
        }

        if (InsideAttributeBrackets(tokens, last))
        {
            return new TanCompletionContext(CompletionContextKind.Attributes, prefix, prefixStart);
        }

        TokenKind governing = GoverningKeyword(tokens, last);
        CompletionContextKind? fromKeyword = governing switch
        {
            TokenKind.In or TokenKind.Layer => CompletionContextKind.Layers,
            TokenKind.Extends or TokenKind.To or TokenKind.From => CompletionContextKind.Contracts,
            TokenKind.Inherits or TokenKind.Implements or TokenKind.Injects or TokenKind.Returns or TokenKind.New =>
                CompletionContextKind.TypeNames,
            TokenKind.Using => CompletionContextKind.Namespaces,
            _ => null,
        };

        if (fromKeyword is CompletionContextKind kind)
        {
            return new TanCompletionContext(kind, prefix, prefixStart);
        }

        return new TanCompletionContext(BlockContext(tokens, wordIndex < 0 ? last + 1 : wordIndex), prefix, prefixStart);
    }

    private static bool IsWordLike(TokenKind kind) =>
        kind is TokenKind.Identifier or TokenKind.Glob or TokenKind.Template || TokenKinds.IsKeyword(kind);

    private static bool InsideAttributeBrackets(Token[] tokens, int last)
    {
        for (int i = last; i >= 0; i--)
        {
            switch (tokens[i].Kind)
            {
                case TokenKind.LeftBracket:
                    return true;
                case TokenKind.RightBracket:
                case TokenKind.LeftBrace:
                case TokenKind.RightBrace:
                case TokenKind.Semicolon:
                    return false;
                default:
                    break;
            }
        }

        return false;
    }

    private static TokenKind GoverningKeyword(Token[] tokens, int last)
    {
        for (int i = last; i >= 0; i--)
        {
            TokenKind kind = tokens[i].Kind;
            switch (kind)
            {
                case TokenKind.Identifier:
                case TokenKind.Glob:
                case TokenKind.Template:
                case TokenKind.Star:
                case TokenKind.DoubleStar:
                case TokenKind.LessThan:
                case TokenKind.GreaterThan:
                case TokenKind.Comma:
                case TokenKind.Pipe:
                case TokenKind.Only:
                    continue;
                default:
                    return kind;
            }
        }

        return TokenKind.EndOfFile;
    }

    private static CompletionContextKind BlockContext(Token[] tokens, int limit)
    {
        var stack = new Stack<Block>();
        Block pending = Block.Unknown;
        bool hasPending = false;

        for (int i = 0; i < limit && i < tokens.Length; i++)
        {
            TokenKind kind = tokens[i].Kind;
            if (kind == TokenKind.EndOfFile) break;

            if (kind == TokenKind.Project) { pending = Block.Project; hasPending = true; }
            else if (kind == TokenKind.Contract) { pending = Block.Contract; hasPending = true; }
            else if (TokenKinds.IsMemberKind(kind)) { pending = Block.Member; hasPending = true; }
            else if (kind == TokenKind.LeftBrace)
            {
                stack.Push(hasPending ? pending : Block.Unknown);
                hasPending = false;
            }
            else if (kind == TokenKind.RightBrace)
            {
                if (stack.Count > 0) stack.Pop();
                hasPending = false;
            }
        }

        if (stack.Count == 0) return CompletionContextKind.TopLevel;
        return stack.Peek() == Block.Project ? CompletionContextKind.ProjectBody : CompletionContextKind.ContractBody;
    }
}
