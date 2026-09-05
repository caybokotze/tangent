using BenchmarkDotNet.Attributes;
using Tangent.Core.Matching;
using Tangent.Core.Text;

namespace Tangent.Benchmarks;

/// <summary>
/// §12.14 and §12.8: glob match hit and miss. Every benchmark here must report <b>0 B allocated</b> —
/// the matcher splits into a <c>stackalloc int[64]</c> and walks segments, so a match never touches the
/// heap for the namespace shapes a real solution produces.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(TangentConfig))]
public class GlobBenchmarks
{
    private const string Hit = "Orders.Domain.Entities.Pricing";
    private const string Miss = "Orders.Infrastructure.Repositories";
    private const string MiddleHit = "Orders.Application.Handlers.Queries.Internal";
    private const string StarHit = "Orders.Domain.Internal.Pricing";
    private const string StarMiss = "Orders.Domain.Deep.Internal.Pricing";

    private Glob _prefix = null!;      // Orders.Domain.**
    private Glob _middle = null!;      // Orders.**.Internal
    private Glob _stars = null!;       // Orders.*.Internal.*

    /// <summary>Compiles the patterns once; compilation is not what is being measured.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var strings = new StringTable();
        _prefix = Glob.Compile("Orders.Domain.**".AsSpan(), strings);
        _middle = Glob.Compile("Orders.**.Internal".AsSpan(), strings);
        _stars = Glob.Compile("Orders.*.Internal.*".AsSpan(), strings);
    }

    /// <summary><c>Orders.Domain.**</c> against a namespace inside the Domain layer.</summary>
    [Benchmark]
    public bool PrefixHit() => _prefix.Matches(Hit.AsSpan());

    /// <summary><c>Orders.Domain.**</c> against a namespace in a sibling layer.</summary>
    [Benchmark]
    public bool PrefixMiss() => _prefix.Matches(Miss.AsSpan());

    /// <summary><c>Orders.**.Internal</c> — the backtracking path, matching.</summary>
    [Benchmark]
    public bool DoubleStarInMiddleHit() => _middle.Matches(MiddleHit.AsSpan());

    /// <summary><c>Orders.**.Internal</c> — the backtracking path, exhausted without a match.</summary>
    [Benchmark]
    public bool DoubleStarInMiddleMiss() => _middle.Matches(Hit.AsSpan());

    /// <summary><c>Orders.*.Internal.*</c> — single-segment wildcards, matching.</summary>
    [Benchmark]
    public bool StarSegmentsHit() => _stars.Matches(StarHit.AsSpan());

    /// <summary><c>Orders.*.Internal.*</c> — single-segment wildcards, wrong segment count.</summary>
    [Benchmark]
    public bool StarSegmentsMiss() => _stars.Matches(StarMiss.AsSpan());
}
