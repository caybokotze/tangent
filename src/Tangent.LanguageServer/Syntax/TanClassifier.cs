using System.Collections.Immutable;
using Tangent.Core.Syntax;
using Tangent.Core.Text;

namespace Tangent.LanguageServer.Syntax;

/// <summary>
/// Turns a parsed <c>.tan</c> file into a flat, position-sorted list of classified regions.
/// Everything positional the server does — semantic tokens, hover, definition, references —
/// reads this list, so classification lives in exactly one place and is derived from the
/// Core token stream and AST rather than from regular expressions.
/// </summary>
internal static class TanClassifier
{
    /// <summary>Classifies one syntax tree.</summary>
    public static ImmutableArray<TanRegion> Classify(SyntaxTree tree)
    {
        string text = tree.Source.Text;
        Token[] tokens = tree.Tokens;
        var byStart = new Dictionary<int, TanRegion>(tokens.Length + 8);

        AddTokenRegions(byStart, tokens, text);
        AddCommentRegions(byStart, tokens, text);
        AddSyntaxRegions(byStart, tree, tokens, text);

        var regions = byStart.Values.ToList();
        regions.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        return regions.ToImmutableArray();
    }

    /// <summary>The region containing or immediately after <paramref name="offset"/>, or null.</summary>
    public static TanRegion? FindAt(ImmutableArray<TanRegion> regions, int offset)
    {
        if (regions.IsDefaultOrEmpty) return null;

        int low = 0;
        int high = regions.Length - 1;
        int candidate = -1;
        while (low <= high)
        {
            int mid = low + ((high - low) / 2);
            if (regions[mid].Start <= offset)
            {
                candidate = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        // Prefer a region that strictly contains the offset, then one that ends exactly on it.
        for (int i = candidate; i >= 0 && i >= candidate - 2; i--)
        {
            if (i < 0) break;
            TanRegion region = regions[i];
            if (offset >= region.Start && offset < region.End) return region;
        }

        if (candidate >= 0 && regions[candidate].Touches(offset)) return regions[candidate];
        return null;
    }

    private static void Put(Dictionary<int, TanRegion> map, int start, int length, TanRegionKind kind, string text)
    {
        if (length <= 0 || start < 0 || start + length > text.Length) return;
        map[start] = new TanRegion(start, length, kind, text.Substring(start, length));
    }

    private static void AddTokenRegions(Dictionary<int, TanRegion> map, Token[] tokens, string text)
    {
        foreach (Token token in tokens)
        {
            switch (token.Kind)
            {
                case TokenKind.EndOfFile:
                case TokenKind.Unknown:
                case TokenKind.Identifier:
                case TokenKind.LeftBrace:
                case TokenKind.RightBrace:
                case TokenKind.LeftBracket:
                case TokenKind.RightBracket:
                case TokenKind.LeftParen:
                case TokenKind.RightParen:
                case TokenKind.Semicolon:
                    break;

                case TokenKind.StringLiteral:
                    Put(map, token.Start, token.Length, TanRegionKind.StringLiteral, text);
                    break;

                case TokenKind.Glob:
                    Put(map, token.Start, token.Length, TanRegionKind.Glob, text);
                    break;

                case TokenKind.Template:
                    Put(map, token.Start, token.Length, TanRegionKind.TypeTemplate, text);
                    break;

                case TokenKind.Star:
                case TokenKind.DoubleStar:
                case TokenKind.LessThan:
                case TokenKind.GreaterThan:
                case TokenKind.Comma:
                case TokenKind.Pipe:
                case TokenKind.Colon:
                case TokenKind.Arrow:
                case TokenKind.Equals:
                    Put(map, token.Start, token.Length, TanRegionKind.Operator, text);
                    break;

                default:
                    if (TokenKinds.IsKeyword(token.Kind))
                    {
                        Put(map, token.Start, token.Length, TanRegionKind.Keyword, text);
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// Comments are skipped by the lexer, so they are recovered from the gaps between tokens.
    /// A gap can only hold whitespace and comments, which makes the scan exact.
    /// </summary>
    private static void AddCommentRegions(Dictionary<int, TanRegion> map, Token[] tokens, string text)
    {
        int cursor = 0;
        for (int i = 0; i <= tokens.Length; i++)
        {
            int gapEnd = i < tokens.Length ? tokens[i].Start : text.Length;
            if (gapEnd > text.Length) gapEnd = text.Length;
            ScanComments(map, text, cursor, gapEnd);
            if (i < tokens.Length)
            {
                int end = tokens[i].End;
                if (end > cursor) cursor = end;
            }
        }
    }

    private static void ScanComments(Dictionary<int, TanRegion> map, string text, int from, int to)
    {
        int i = from;
        while (i < to - 1)
        {
            if (text[i] == '/' && text[i + 1] == '/')
            {
                int end = i;
                while (end < to && text[end] != '\n' && text[end] != '\r') end++;
                Put(map, i, end - i, TanRegionKind.Comment, text);
                i = end;
            }
            else if (text[i] == '/' && text[i + 1] == '*')
            {
                int end = i + 2;
                while (end < to - 1 && !(text[end] == '*' && text[end + 1] == '/')) end++;
                end = end < to - 1 ? end + 2 : to;
                Put(map, i, end - i, TanRegionKind.Comment, text);
                i = end;
            }
            else
            {
                i++;
            }
        }
    }

    private static void AddSyntaxRegions(Dictionary<int, TanRegion> map, SyntaxTree tree, Token[] tokens, string text)
    {
        var tokenIndexByStart = new Dictionary<int, int>(tokens.Length);
        for (int i = 0; i < tokens.Length; i++) tokenIndexByStart[tokens[i].Start] = i;

        Walk(map, tree, tokens, tokenIndexByStart, text, tree.Root);
    }

    private static void Walk(
        Dictionary<int, TanRegion> map,
        SyntaxTree tree,
        Token[] tokens,
        Dictionary<int, int> tokenIndexByStart,
        string text,
        int nodeIndex)
    {
        if (nodeIndex < 0) return;
        NodeList nodes = tree.Nodes;
        Node node = nodes[nodeIndex];

        switch (node.Kind)
        {
            case NodeKind.Contract:
                ClassifyContractHeader(map, tokens, tokenIndexByStart, text, node.Span);
                break;

            case NodeKind.LayerDecl:
                ClassifyAfterKeyword(map, tokens, tokenIndexByStart, text, node.Span, TanRegionKind.LayerDeclaration);
                break;

            case NodeKind.Dependency:
                ClassifyTokenAt(map, tokens, tokenIndexByStart, text, node.Span.Start, TokenKind.Identifier, TanRegionKind.LayerReference);
                break;

            case NodeKind.LayerRef:
                Put(map, node.Span.Start, node.Span.Length, TanRegionKind.LayerReference, text);
                break;

            case NodeKind.ContractRef:
                Put(map, node.Span.Start, node.Span.Length, TanRegionKind.ContractReference, text);
                break;

            case NodeKind.Bind:
            case NodeKind.Exempt:
                ClassifyAfterKeyword(map, tokens, tokenIndexByStart, text, node.Span, TanRegionKind.BoundTypeName);
                break;

            case NodeKind.TypePattern:
                PutName(map, tree, text, node, TanRegionKind.TypePattern);
                break;

            case NodeKind.TypeTemplate:
                PutName(map, tree, text, node, TanRegionKind.TypeTemplate);
                break;

            case NodeKind.AttrRef:
                PutName(map, tree, text, node, TanRegionKind.AttributeName);
                break;

            case NodeKind.ExceptAttribute:
                ClassifyAfterKeyword(map, tokens, tokenIndexByStart, text, node.Span, TanRegionKind.AttributeName);
                break;

            case NodeKind.PredInterfaceLivesIn:
                ClassifyLivesIn(map, tokens, tokenIndexByStart, text, node.Span);
                break;

            default:
                break;
        }

        foreach (int child in nodes.Children(nodeIndex))
        {
            Walk(map, tree, tokens, tokenIndexByStart, text, child);
        }
    }

    /// <summary>Classifies the name token of a node whose payload is an interned name starting at the node span.</summary>
    private static void PutName(Dictionary<int, TanRegion> map, SyntaxTree tree, string text, Node node, TanRegionKind kind)
    {
        var id = new StringId(node.Payload);
        if (!id.IsValid) return;
        int length = tree.Strings.GetSpan(id).Length;
        if (length <= 0 || length > node.Span.Length) length = node.Span.Length;
        Put(map, node.Span.Start, length, kind, text);
    }

    private static void ClassifyTokenAt(
        Dictionary<int, TanRegion> map,
        Token[] tokens,
        Dictionary<int, int> tokenIndexByStart,
        string text,
        int start,
        TokenKind expected,
        TanRegionKind kind)
    {
        if (!tokenIndexByStart.TryGetValue(start, out int index)) return;
        if (tokens[index].Kind != expected) return;
        Put(map, tokens[index].Start, tokens[index].Length, kind, text);
    }

    /// <summary>Classifies the identifier that follows the keyword a node starts with.</summary>
    private static void ClassifyAfterKeyword(
        Dictionary<int, TanRegion> map,
        Token[] tokens,
        Dictionary<int, int> tokenIndexByStart,
        string text,
        SourceSpan span,
        TanRegionKind kind)
    {
        if (!tokenIndexByStart.TryGetValue(span.Start, out int index)) return;
        int next = index + 1;
        if (next >= tokens.Length || tokens[next].Kind != TokenKind.Identifier) return;
        Put(map, tokens[next].Start, tokens[next].Length, kind, text);
    }

    private static void ClassifyContractHeader(
        Dictionary<int, TanRegion> map,
        Token[] tokens,
        Dictionary<int, int> tokenIndexByStart,
        string text,
        SourceSpan span)
    {
        if (!tokenIndexByStart.TryGetValue(span.Start, out int index)) return;
        int i = index + 1;
        if (i < tokens.Length && tokens[i].Kind == TokenKind.Identifier)
        {
            Put(map, tokens[i].Start, tokens[i].Length, TanRegionKind.ContractDeclaration, text);
            i++;
        }

        if (i < tokens.Length && tokens[i].Kind == TokenKind.Extends)
        {
            i++;
            if (i < tokens.Length && tokens[i].Kind == TokenKind.Identifier)
            {
                Put(map, tokens[i].Start, tokens[i].Length, TanRegionKind.ContractReference, text);
            }
        }
    }

    private static void ClassifyLivesIn(
        Dictionary<int, TanRegion> map,
        Token[] tokens,
        Dictionary<int, int> tokenIndexByStart,
        string text,
        SourceSpan span)
    {
        if (!tokenIndexByStart.TryGetValue(span.Start, out int index)) return;
        for (int i = index; i < tokens.Length && tokens[i].Start < span.End; i++)
        {
            if (tokens[i].Kind != TokenKind.In) continue;
            if (i + 1 < tokens.Length && tokens[i + 1].Kind == TokenKind.Identifier)
            {
                Put(map, tokens[i + 1].Start, tokens[i + 1].Length, TanRegionKind.LayerReference, text);
            }

            return;
        }
    }
}
