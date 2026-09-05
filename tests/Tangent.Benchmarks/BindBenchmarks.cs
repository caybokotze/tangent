using System;
using System.Collections.Immutable;
using BenchmarkDotNet.Attributes;
using Tangent.Core;
using Tangent.Core.Binding;
using Tangent.Core.Ir;
using Tangent.Core.Syntax;
using Tangent.Core.Text;

namespace Tangent.Benchmarks;

/// <summary>
/// §12.14: bind of 30 contracts. The corpus is generated: five contract shapes cycled, six layers with a
/// full dependency matrix, and an `extends` chain on every fifth contract so rule flattening and the
/// topological sort are exercised.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(TangentConfig))]
public class BindBenchmarks
{
    private ImmutableArray<SourceFile> _sources;
    private ImmutableArray<SyntaxTree> _parsed;

    /// <summary>Loads the corpus and verifies it binds cleanly before anything is measured.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _sources = ContractSources.ThirtyContracts;

        BindResult result = ModelLoader.Load(_sources.AsSpan());
        if (result.Model is null || result.HasErrors)
        {
            throw new InvalidOperationException(
                "The generated 30-contract corpus does not bind cleanly: " +
                string.Join(Environment.NewLine, result.Diagnostics));
        }

        if (result.Model.Contracts.Length != 30)
        {
            throw new InvalidOperationException($"Expected 30 contracts, bound {result.Model.Contracts.Length}.");
        }

        if (result.Model.Layers.Length != 6)
        {
            throw new InvalidOperationException($"Expected 6 layers, bound {result.Model.Layers.Length}.");
        }

        _parsed = ModelLoader.ParseAll(_sources.AsSpan());
    }

    /// <summary>Parse plus bind, which is what <c>ContractFiles.LoadModel</c> does on a cache miss.</summary>
    [Benchmark(Baseline = true)]
    public int LoadModel()
    {
        BindResult result = ModelLoader.Load(_sources.AsSpan());
        return result.Model!.Contracts.Length;
    }

    /// <summary>The bind step alone, over an already-parsed forest sharing one string table.</summary>
    [Benchmark]
    public int Bind()
    {
        BindResult result = Binder.Bind(_parsed);
        ArchitectureModel model = result.Model!;
        return model.Contracts.Length + model.Layers.Length;
    }
}
