using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Tangent.Core.Diagnostics;
using Tangent.Core.Syntax;
using Tangent.Core.Text;

namespace Tangent.Benchmarks;

/// <summary>
/// §12.14: lex and parse of a ~500-line `.tan` file. The file is the sample contracts replicated with
/// unique names, so the token mix (globs, templates, type patterns, attribute lists) is realistic.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(TangentConfig))]
public class LexParseBenchmarks
{
    private SourceFile _file;
    private string _text = string.Empty;
    private List<TangentDiagnostic> _diagnostics = new();

    /// <summary>Materialises the corpus outside the measured region.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _file = ContractSources.Large;
        _text = _file.Text;
        _diagnostics = new List<TangentDiagnostic>(16);

        // Fail loudly rather than benchmarking a file full of error recovery.
        Token[] tokens = Lexer.Lex(_text.AsSpan(), 0, _file.Path, _diagnostics);
        if (_diagnostics.Count != 0) throw new InvalidOperationException("The generated corpus does not lex cleanly.");
        if (tokens.Length < 1000) throw new InvalidOperationException("The generated corpus is unexpectedly small.");
    }

    /// <summary>Tokenises the file. The lexer never substrings; the only allocation is the token array.</summary>
    [Benchmark]
    public int Lex()
    {
        _diagnostics.Clear();
        return Lexer.Lex(_text.AsSpan(), 0, _file.Path, _diagnostics).Length;
    }

    /// <summary>Lexes and parses the file into a flat AST with a fresh string table.</summary>
    [Benchmark]
    public int LexAndParse()
    {
        SyntaxTree tree = Parser.Parse(_file, 0, new StringTable());
        return tree.Nodes.Count;
    }
}
