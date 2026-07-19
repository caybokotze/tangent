using System;
using System.Collections.Immutable;
using Tangent.Core.Binding;
using Tangent.Core.Syntax;
using Tangent.Core.Text;

namespace Tangent.Core;

/// <summary>Lexes, parses and binds a whole <c>.tangent</c> folder in one call.</summary>
public static class ModelLoader
{
    /// <summary>
    /// Parses every file into one compilation unit sharing a single <see cref="StringTable"/>, then binds them.
    /// Parse diagnostics are merged into the result.
    /// </summary>
    public static BindResult Load(ReadOnlySpan<SourceFile> files)
    {
        ImmutableArray<SyntaxTree> trees = ParseAll(files);
        return Binder.Bind(trees);
    }

    /// <summary>Parses every file into one compilation unit without binding.</summary>
    public static ImmutableArray<SyntaxTree> ParseAll(ReadOnlySpan<SourceFile> files)
    {
        var strings = new StringTable();
        var trees = ImmutableArray.CreateBuilder<SyntaxTree>(files.Length);
        for (int i = 0; i < files.Length; i++)
        {
            trees.Add(Parser.Parse(files[i], i, strings));
        }

        return trees.ToImmutable();
    }

    /// <summary>Parses one file on its own, with a fresh string table. Useful for editors and goldens.</summary>
    public static SyntaxTree ParseOne(SourceFile file) => Parser.Parse(file, 0, new StringTable());
}
