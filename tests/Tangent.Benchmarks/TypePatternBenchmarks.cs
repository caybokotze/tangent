using System;
using System.Collections.Immutable;
using BenchmarkDotNet.Attributes;
using Tangent.Core;
using Tangent.Core.Binding;
using Tangent.Core.Ir;
using Tangent.Core.Matching;
using Tangent.Core.Text;

namespace Tangent.Benchmarks;

/// <summary>
/// The Roslyn-free display-name matcher (`TypePatternTable.MatchesGroup`), which the CLI, the goldens and
/// the language server use. The group comes out of a real bound model so the table layout matches what
/// the analyzer walks. Every benchmark here must report <b>0 B allocated</b>.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(TangentConfig))]
public class TypePatternBenchmarks
{
    private const string GroupSource = """
        contract Probe {
            match: class in Orders.Api.**

            methods where public {
                returns Task<ActionResult<*>> | Task<IActionResult>
            }
        }
        """;

    private const string Hit = "Task<ActionResult<OrderDto>>";
    private const string Miss = "Task<ActionResult<OrderDto, int>>";
    private const string ShapeMiss = "System.Collections.Generic.IEnumerable<ReportViewModel>";

    private TypePatternTable _patterns = null!;
    private int _group = -1;

    /// <summary>Binds the probe contract and finds the `returns` group index.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var files = ImmutableArray.Create(new SourceFile(".tangent/probe.tan", GroupSource));
        BindResult result = ModelLoader.Load(files.AsSpan());
        if (result.Model is null || result.HasErrors)
        {
            throw new InvalidOperationException(
                "The probe contract does not bind: " + string.Join(Environment.NewLine, result.Diagnostics));
        }

        ArchitectureModel model = result.Model;
        _patterns = model.TypePatterns;

        ImmutableArray<Rule> rules = model.Contracts[0].Rules;
        for (int i = 0; i < rules.Length; i++)
        {
            if (rules[i].Predicate.Kind != PredicateKind.Returns) continue;
            _group = rules[i].Predicate.Payload;
            break;
        }

        if (_group < 0) throw new InvalidOperationException("No `returns` rule was bound.");
        if (!_patterns.MatchesGroup(_group, Hit.AsSpan())) throw new InvalidOperationException("Expected a hit.");
        if (_patterns.MatchesGroup(_group, Miss.AsSpan())) throw new InvalidOperationException("Expected a miss.");
    }

    /// <summary><c>Task&lt;ActionResult&lt;*&gt;&gt;</c> against a conforming return type.</summary>
    [Benchmark]
    public bool GroupHit() => _patterns.MatchesGroup(_group, Hit.AsSpan());

    /// <summary>Same shape, wrong arity on the inner argument — the alternation is exhausted.</summary>
    [Benchmark]
    public bool GroupMissArity() => _patterns.MatchesGroup(_group, Miss.AsSpan());

    /// <summary>A completely different, dotted, generic type — the realistic violation shape.</summary>
    [Benchmark]
    public bool GroupMissShape() => _patterns.MatchesGroup(_group, ShapeMiss.AsSpan());
}
