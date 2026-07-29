using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Tangent.Analyzers.Tests;

/// <summary>
/// Shared plumbing for the analyzer and code-fix tests: the reference assemblies, the self-contained
/// stub sources (the Tangent attributes plus a tiny ASP.NET-like framework) and the <c>.tan</c>
/// additional file every test supplies.
/// </summary>
internal static class Harness
{
    /// <summary>The path the contract file is given inside the test compilation.</summary>
    internal const string ContractPath = ".tangent/contracts.tan";

    /// <summary>The path diagnostics reported into the contract file carry.</summary>
    internal const string ContractDiagnosticPath = ContractPath;

    internal static ReferenceAssemblies References => ReferenceAssemblies.Net.Net80;

    /// <summary>Runs the analyzer over one source file with one contract file.</summary>
    internal static Task AnalyzerAsync(string contracts, string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<TangentAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = References,
            TestCode = source,
        };

        test.TestState.Sources.Add(("Stubs.cs", Stubs.Source));
        test.TestState.AdditionalFiles.Add((ContractPath, contracts));
        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync(CancellationToken.None);
    }

    /// <summary>Runs the analyzer over several source files with one contract file.</summary>
    internal static Task AnalyzerAsync(string contracts, (string Name, string Content)[] sources, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<TangentAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = References,
        };

        foreach ((string name, string content) in sources) test.TestState.Sources.Add((name, content));
        test.TestState.Sources.Add(("Stubs.cs", Stubs.Source));
        test.TestState.AdditionalFiles.Add((ContractPath, contracts));
        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync(CancellationToken.None);
    }

    /// <summary>Runs the analyzer with no contract file at all.</summary>
    internal static Task NoContractsAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<TangentAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = References,
            TestCode = source,
        };

        test.TestState.Sources.Add(("Stubs.cs", Stubs.Source));
        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync(CancellationToken.None);
    }

    /// <summary>Runs the analyzer and the code-fix provider, asserting the fixed source.</summary>
    internal static Task CodeFixAsync(string contracts, string source, string fixedSource, params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<TangentAnalyzer, TangentCodeFixProvider, DefaultVerifier>
        {
            ReferenceAssemblies = References,
            TestCode = source,
            FixedCode = fixedSource,
        };

        test.TestState.Sources.Add(("Stubs.cs", Stubs.Source));
        test.TestState.AdditionalFiles.Add((ContractPath, contracts));
        test.FixedState.Sources.Add(("Stubs.cs", Stubs.Source));
        test.FixedState.AdditionalFiles.Add((ContractPath, contracts));
        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync(CancellationToken.None);
    }

    /// <summary>A diagnostic expected inside the <c>.tan</c> file.</summary>
    internal static DiagnosticResult Contract(string code, int line, int column, int endLine, int endColumn) =>
        new DiagnosticResult(code, TangentDiagnostics.Get(code).DefaultSeverity)
            .WithSpan(ContractDiagnosticPath, line, column, endLine, endColumn);
}
