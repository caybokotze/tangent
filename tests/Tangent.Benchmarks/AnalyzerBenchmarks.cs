using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Tangent.Analyzers;
using Tangent.Analyzers.Internal;
using Tangent.Core.Binding;
using Tangent.Core.Ir;
using Tangent.Core.Text;

namespace Tangent.Benchmarks;

/// <summary>
/// §12.2 and §12.14: the analyzer's hot path. One in-memory compilation is built in
/// <see cref="Setup"/> with the sample contracts as <c>AdditionalText</c>s and four C# sources: stub
/// ASP.NET/MediatR types, an unbound plain class, a conforming controller and a violating controller.
/// <para>
/// Roslyn does not let a host invoke a registered symbol action directly, so the per-type benchmarks call
/// the analyzer's own internals — <c>CompilationState.GetContext</c> (the binding probe the symbol action
/// starts with) and <c>RuleEvaluator.Evaluate</c> (Tier 0) — through <c>InternalsVisibleTo</c>. That is the
/// only way to see the allocation behaviour of the early-exit path on its own.
/// </para>
/// </summary>
[MemoryDiagnoser]
[Config(typeof(TangentConfig))]
public class AnalyzerBenchmarks
{
    private CSharpCompilation _compilation = null!;
    private AnalyzerOptions _options = null!;
    private CompilationState _state = null!;

    private INamedTypeSymbol _unbound = null!;
    private INamedTypeSymbol _boundClean = null!;
    private INamedTypeSymbol _boundViolating = null!;

    private TypeContext _cleanContext = null!;
    private TypeContext _violatingContext = null!;

    private SymbolAnalysisContext _cleanSymbolContext;
    private SymbolAnalysisContext _violatingSymbolContext;

    private int _discarded;

    /// <summary>Builds the compilation, loads the model, and warms every cache the analyzer keeps.</summary>
    [GlobalSetup]
    public void Setup()
    {
        ImmutableArray<SourceFile> contracts = ContractSources.Sample;

        BindResult result = Tangent.Core.ModelLoader.Load(contracts.AsSpan());
        if (result.Model is null || result.HasErrors)
        {
            throw new InvalidOperationException(
                "The sample contracts do not bind cleanly: " + string.Join(Environment.NewLine, result.Diagnostics));
        }

        ArchitectureModel model = result.Model;

        _compilation = CSharpCompilation.Create(
            "Orders.Bench",
            AnalyzerSources.All.Select(s => CSharpSyntaxTree.ParseText(s)),
            ReferenceAssemblies(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Disable));

        ImmutableArray<Diagnostic> compileErrors = _compilation
            .GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .ToImmutableArray();
        if (!compileErrors.IsEmpty)
        {
            throw new InvalidOperationException(
                "The benchmark compilation does not compile: " + string.Join(Environment.NewLine, compileErrors));
        }

        ImmutableArray<AdditionalText> additional = contracts
            .Select(f => (AdditionalText)new InMemoryAdditionalText(f.Path, f.Text))
            .ToImmutableArray();
        _options = new AnalyzerOptions(additional);

        _state = new CompilationState(_compilation, model, null, false);

        _unbound = Resolve("Contoso.Plumbing.CacheKeyBuilder");
        _boundClean = Resolve("Orders.Api.Controllers.OrdersController");
        _boundViolating = Resolve("Orders.Api.Controllers.ReportsController");

        // Warm the per-compilation namespace cache the way a real compilation start does, so the measured
        // probe sees the steady state rather than the first-sight cost.
        if (_state.GetContext(_unbound) is not null)
        {
            throw new InvalidOperationException("The unbound probe type is bound to something; fix AnalyzerSources.");
        }

        _cleanContext = _state.GetContext(_boundClean)
            ?? throw new InvalidOperationException("OrdersController did not bind to the Controller contract.");
        _violatingContext = _state.GetContext(_boundViolating)
            ?? throw new InvalidOperationException("ReportsController did not bind to the Controller contract.");

        if (!_cleanContext.IsBound || !_violatingContext.IsBound)
        {
            throw new InvalidOperationException("The controller types are in a layer but bound to no contract.");
        }

        _cleanSymbolContext = DiscardingContext(_boundClean);
        _violatingSymbolContext = DiscardingContext(_boundViolating);

        // Sanity: the clean controller must produce nothing and the violating one must produce something.
        int clean = CountDiagnostics(_boundClean, _cleanContext);
        int dirty = CountDiagnostics(_boundViolating, _violatingContext);
        if (clean != 0) throw new InvalidOperationException($"The conforming controller reported {clean} diagnostics.");
        if (dirty == 0) throw new InvalidOperationException("The violating controller reported nothing.");
    }

    // ------------------------------------------------------------------
    // the early-exit path
    // ------------------------------------------------------------------

    /// <summary>
    /// §12.2: the path from the symbol action to "this type is bound to nothing and lives outside every
    /// layer". <b>Must report 0 B allocated.</b>
    /// </summary>
    [Benchmark(Baseline = true)]
    public bool UnboundTypeProbe() => _state.GetContext(_unbound) is null;

    // ------------------------------------------------------------------
    // Tier 0 evaluation
    // ------------------------------------------------------------------

    /// <summary>Tier 0 over a controller that satisfies every rule: no diagnostic is constructed.</summary>
    [Benchmark]
    public void BoundCleanType()
    {
        var evaluator = new RuleEvaluator(_state, _cleanContext, _cleanSymbolContext);
        evaluator.Evaluate();
    }

    /// <summary>
    /// Tier 0 over a controller that breaks several rules. The diagnostics are built in full (message
    /// composition, property bags, locations) and then discarded by the sink, so the allocation column is
    /// the real cost of reporting.
    /// </summary>
    [Benchmark]
    public void BoundViolatingType()
    {
        var evaluator = new RuleEvaluator(_state, _violatingContext, _violatingSymbolContext);
        evaluator.Evaluate();
    }

    // ------------------------------------------------------------------
    // the whole thing
    // ------------------------------------------------------------------

    /// <summary>
    /// The supported public entry point: the whole small compilation through
    /// <c>CompilationWithAnalyzers</c>. The allocation figure includes Roslyn's own driver, so it is
    /// reported but never asserted on.
    /// </summary>
    [Benchmark]
    public int FullCompilationAnalysis()
    {
        CompilationWithAnalyzers withAnalyzers = _compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new TangentAnalyzer()),
            _options);

        return withAnalyzers.GetAnalyzerDiagnosticsAsync(CancellationToken.None).GetAwaiter().GetResult().Length;
    }

    // ------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------

    // CS0618: the public SymbolAnalysisContext constructor is marked obsolete in favour of
    // CompilationWithAnalyzers. That is exactly what FullCompilationAnalysis measures; these two
    // benchmarks exist to isolate Tangent's own cost from the driver's, which needs this constructor.
#pragma warning disable CS0618
    private int CountDiagnostics(INamedTypeSymbol type, TypeContext context)
    {
        int count = 0;
        var symbolContext = new SymbolAnalysisContext(
            type,
            _compilation,
            _options,
            _ => count++,
            _ => true,
            CancellationToken.None);

        var evaluator = new RuleEvaluator(_state, context, symbolContext);
        evaluator.Evaluate();
        return count;
    }

    /// <summary>A context whose sink throws every diagnostic away, so only the analyzer's cost is measured.</summary>
    private SymbolAnalysisContext DiscardingContext(INamedTypeSymbol type) =>
        new SymbolAnalysisContext(
            type,
            _compilation,
            _options,
            Discard,
            Supported,
            CancellationToken.None);

#pragma warning restore CS0618

    private void Discard(Diagnostic diagnostic) => _discarded++;

    private static bool Supported(Diagnostic diagnostic) => true;

    private INamedTypeSymbol Resolve(string metadataName) =>
        _compilation.GetTypeByMetadataName(metadataName)
        ?? throw new InvalidOperationException("Missing benchmark type: " + metadataName);

    private static IEnumerable<MetadataReference> ReferenceAssemblies()
    {
        string assemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        foreach (string path in assemblies.Split(Path.PathSeparator))
        {
            if (path.Length == 0) continue;
            string name = Path.GetFileName(path);
            if (!name.StartsWith("System.", StringComparison.Ordinal) &&
                !string.Equals(name, "netstandard.dll", StringComparison.Ordinal) &&
                !string.Equals(name, "mscorlib.dll", StringComparison.Ordinal))
            {
                continue;
            }

            yield return MetadataReference.CreateFromFile(path);
        }
    }

    private sealed class InMemoryAdditionalText : AdditionalText
    {
        private readonly SourceText _text;

        internal InMemoryAdditionalText(string path, string text)
        {
            Path = path;
            _text = SourceText.From(text, System.Text.Encoding.UTF8);
        }

        public override string Path { get; }

        public override SourceText GetText(CancellationToken cancellationToken = default) => _text;
    }
}
