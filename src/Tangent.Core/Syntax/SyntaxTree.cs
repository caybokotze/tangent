using System;
using System.Collections.Immutable;
using Tangent.Core.Diagnostics;
using Tangent.Core.Text;

namespace Tangent.Core.Syntax;

/// <summary>The result of lexing and parsing one <c>.tan</c> file.</summary>
public sealed class SyntaxTree
{
    /// <summary>Creates a syntax tree.</summary>
    public SyntaxTree(
        SourceFile source,
        int fileIndex,
        Token[] tokens,
        NodeList nodes,
        int root,
        StringTable strings,
        ImmutableArray<TangentDiagnostic> diagnostics)
    {
        Source = source;
        FileIndex = fileIndex;
        Tokens = tokens;
        Nodes = nodes;
        Root = root;
        Strings = strings;
        Diagnostics = diagnostics;
    }

    /// <summary>The file this tree came from.</summary>
    public SourceFile Source { get; }

    /// <summary>The index of <see cref="Source"/> in the compilation unit, used by every <see cref="SourceSpan"/> here.</summary>
    public int FileIndex { get; }

    /// <summary>Every token, ending with <see cref="TokenKind.EndOfFile"/>.</summary>
    public Token[] Tokens { get; }

    /// <summary>The flat node store.</summary>
    public NodeList Nodes { get; }

    /// <summary>The index of the <see cref="NodeKind.File"/> node.</summary>
    public int Root { get; }

    /// <summary>The string table identifiers in this tree were interned into.</summary>
    public StringTable Strings { get; }

    /// <summary>Lexical and syntactic diagnostics, in source order.</summary>
    public ImmutableArray<TangentDiagnostic> Diagnostics { get; }

    /// <summary>The characters a node covers.</summary>
    public ReadOnlySpan<char> TextOf(int nodeIndex)
    {
        SourceSpan span = Nodes[nodeIndex].Span;
        if (span.Start < 0 || span.End > Source.Text.Length) return ReadOnlySpan<char>.Empty;
        return Source.Span.Slice(span.Start, span.Length);
    }
}
