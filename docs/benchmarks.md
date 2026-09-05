# Benchmarks

`tests/Tangent.Benchmarks` is the BenchmarkDotNet harness that enforces §12 of `TANGENT-PLAN.md`. It
measures the two things that decide whether Tangent is usable — how long a `.tan` folder takes to become
a model, and what the analyzer costs per named type inside the compiler — and it is the only place where
the "unbound types cost nothing" rule (§12.2) is actually verified.

## Running

```sh
dotnet run -c Release --project tests/Tangent.Benchmarks -- --filter '*'
```

| Switch | Effect |
|---|---|
| *(none)* | `ShortRun` (1 launch, 3 warmups, 3 iterations). A full sweep takes about three minutes. |
| `--long` | BenchmarkDotNet's default job. Use this for numbers that get committed or compared. |
| `TANGENT_BENCH_LONG=1` | Same as `--long`, for CI. |
| `--filter '*Glob*'` | One class. Every other BenchmarkDotNet switch works as usual. |

`--long` is consumed by `Program.cs` before the arguments reach `BenchmarkSwitcher`, which rejects
anything it does not recognise. Everything is `[MemoryDiagnoser]`, so the **Allocated** column is always
present; results land in `BenchmarkDotNet.Artifacts/` (git-ignored).

## The corpora

| Corpus | What it is |
|---|---|
| Sample | The six files of `samples/Orders/.tangent`, copied next to the benchmark assembly by the project file. The samples stay the single source of truth. |
| Large | 558 lines / ~16 KB, generated at startup: the `project` block once, then the sample contracts replicated five times with contract names suffixed (`Controller`, `Controller_1`, …). |
| Thirty contracts | Generated from templates: a `project` block with **six** layers and a full dependency matrix, plus 30 contracts cycling five shapes (controller, entity, repository with `I{Name}` templates, stateless service, and an `extends` handler on every fifth). |

Both generated corpora are validated in `[GlobalSetup]` — a corpus that does not lex, parse or bind
cleanly throws rather than producing a meaninglessly fast number.

## Results

Recorded with the short job on the machine below. Treat them as shape, not as absolutes; the useful
columns are **Allocated** and the ratios between rows.

```
BenchmarkDotNet v0.15.2, Windows 11 (10.0.26200.9457)
.NET SDK 10.0.112
  [Host]   : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX2
  ShortRun : IterationCount=3  LaunchCount=1  WarmupCount=3
```

### `LexParseBenchmarks` — a 558-line `.tan` file

| Method      | Mean     | Error    | StdDev   | Gen0    | Gen1   | Allocated |
|------------ |---------:|---------:|---------:|--------:|-------:|----------:|
| Lex         | 27.67 μs | 27.24 μs | 1.493 μs |  5.1575 |      - |  31.92 KB |
| LexAndParse | 78.97 μs | 21.75 μs | 1.192 μs | 19.7754 | 4.8828 | 122.35 KB |

Lexing 16 KB of source allocates 32 KB — that is the `Token[]` and nothing else; the lexer never
substrings (§12.4). Parsing adds the flat node array and a fresh `StringTable`.

### `BindBenchmarks` — 30 contracts, 6 layers

| Method    | Mean      | Error    | StdDev   | Ratio | Gen0    | Gen1    | Allocated |
|---------- |----------:|---------:|---------:|------:|--------:|--------:|----------:|
| LoadModel | 166.78 μs | 89.85 μs | 4.925 μs |  1.00 | 54.1992 | 15.3809 | 333.13 KB |
| Bind      |  94.62 μs | 19.84 μs | 1.088 μs |  0.57 | 34.1797 |  6.4697 |  209.9 KB |

`LoadModel` is parse + bind, which is what `ContractFiles.LoadModel` does on a `ModelCache` miss — about
170 μs **once per unique contract content, process-wide** (§12.3). `Bind` alone is the immutable IR being
built: rule flattening over the `extends` chains, the packed dependency matrix and the side tables.

### `GlobBenchmarks` — namespace matching

| Method                 | Mean     | Error     | StdDev   | Allocated |
|----------------------- |---------:|----------:|---------:|----------:|
| PrefixHit              | 33.27 ns |  4.808 ns | 0.264 ns |         - |
| PrefixMiss             | 33.65 ns | 13.063 ns | 0.716 ns |         - |
| DoubleStarInMiddleHit  | 64.34 ns | 68.006 ns | 3.728 ns |         - |
| DoubleStarInMiddleMiss | 51.03 ns | 28.875 ns | 1.583 ns |         - |
| StarSegmentsHit        | 43.39 ns | 13.532 ns | 0.742 ns |         - |
| StarSegmentsMiss       | 40.09 ns | 19.440 ns | 1.066 ns |         - |

**0 B on every row**, as §12.8 requires. `PrefixHit`/`PrefixMiss` are `Orders.Domain.**`;
`DoubleStarInMiddle*` is `Orders.**.Internal` (the backtracking loop); `StarSegments*` is
`Orders.*.Internal.*`. A miss is not more expensive than a hit, so a solution whose namespaces mostly
fall outside the declared layers pays nothing extra.

### `TypePatternBenchmarks` — the Roslyn-free display-name matcher

| Method         | Mean      | Error     | StdDev   | Allocated |
|--------------- |----------:|----------:|---------:|----------:|
| GroupHit       |  84.70 ns |  5.733 ns | 0.314 ns |         - |
| GroupMissArity | 190.39 ns | 75.431 ns | 4.135 ns |         - |
| GroupMissShape |  44.64 ns | 22.009 ns | 1.206 ns |         - |

The group is `Task<ActionResult<*>> | Task<IActionResult>`, lifted out of a real bound model.
`GroupHit` is `Task<ActionResult<OrderDto>>`; `GroupMissArity` is `Task<ActionResult<OrderDto, int>>`,
which has to walk and reject both alternatives; `GroupMissShape` is
`System.Collections.Generic.IEnumerable<ReportViewModel>`, rejected on the outer name. **0 B** throughout.

### `AnalyzerBenchmarks` — the symbol action

One in-memory `CSharpCompilation` holds stub ASP.NET Core / MediatR / EF Core types, an unbound plain
class (`Contoso.Plumbing.CacheKeyBuilder`), a conforming controller and a violating one, with the six
sample `.tan` files supplied as `AdditionalText`s.

| Method                  | Mean           | Error           | StdDev          | Ratio     | Gen0     | Gen1    | Allocated |
|------------------------ |---------------:|----------------:|----------------:|----------:|---------:|--------:|----------:|
| UnboundTypeProbe        |       137.2 ns |        147.1 ns |         8.06 ns |      1.00 |        - |       - |     **-** |
| BoundCleanType          |     1,340.5 ns |        741.5 ns |        40.65 ns |      9.79 |   0.0801 |       - |     504 B |
| BoundViolatingType      |    15,680.3 ns |      6,916.3 ns |       379.10 ns |    114.55 |   3.5095 |       - |   22048 B |
| FullCompilationAnalysis | 8,097,447.4 ns | 21,186,247.0 ns | 1,161,289.69 ns | 59,157.30 | 109.3750 | 15.6250 |  705578 B |

Roslyn gives a host no way to invoke a registered symbol action directly, so the three per-type rows go
through the analyzer's internals (`InternalsVisibleTo` for `Tangent.Benchmarks` is already configured on
both `Tangent.Core` and `Tangent.Analyzers`):

- **`UnboundTypeProbe`** calls `CompilationState.GetContext`, which is the first thing
  `TangentAnalyzer.AnalyzeNamedType` does. `null` means "bound to nothing, outside every layer" and the
  symbol action returns immediately.
- **`BoundCleanType`** / **`BoundViolatingType`** run `RuleEvaluator.Evaluate` (Tier 0) against a
  `SymbolAnalysisContext` whose diagnostic sink discards, so the number is Tangent's cost and not the
  driver's. The `TypeContext` is already cached, exactly as it is on the second and later visits inside a
  real compilation.
- **`FullCompilationAnalysis`** is the supported public path —
  `compilation.WithAnalyzers(...).GetAnalyzerDiagnosticsAsync()` over the whole (tiny) compilation. Its
  allocation figure is dominated by Roslyn's own driver and is reported, never asserted on.

## Allocation findings

### The unbound path is allocation-free — verified

`UnboundTypeProbe` reports **`-` (0 B allocated)**, which is what §12.2 demands. The probe walks every
contract's `match` clause over a `stackalloc int[32]`, the namespace name and its layer index come from a
`ConcurrentDictionary` keyed by `INamespaceSymbol` with `SymbolEqualityComparer.Default`, and no
`TypeContext` is constructed. A direct `GC.GetAllocatedBytesForCurrentThread()` measurement over 1000
iterations agrees: 0.0 B/op.

Note that the very first visit to a namespace does allocate — `CompilationState.GetNamespaceInfo`
(`src/Tangent.Analyzers/Internal/CompilationState.cs:105-107`) calls `ns.ToDisplayString()` and builds one
`NamespaceInfo`. That is once per namespace per compilation, by design (§12.8), and the benchmark warms it
in `[GlobalSetup]` so the steady state is what gets measured.

### The bound-clean path allocates ~504 B per type, all of it Roslyn's

This is **not** a §12 violation — the rule only covers unbound types — but it is worth recording, because
it is the per-type cost every bound type pays even when nothing is wrong. Measured with
`GC.GetAllocatedBytesForCurrentThread()`, the 504 B decomposes as:

| Source | Cost | Times per clean controller |
|---|---|---|
| `ISymbol.Locations` via `SymbolFacts.LocationOf` — `src/Tangent.Analyzers/Internal/SymbolFacts.cs:218-227` | ~45 B/call | 8 (one per matching *member × rule* pair) + 3 type-level |
| `INamedTypeSymbol.GetMembers()` — Roslyn's public-model wrapper | 64 B/call | 1–3 |

Neither allocation is Tangent's own: `LocationOf` reads `symbol.Locations`, which most Roslyn source
symbols materialise as a fresh single-element `ImmutableArray<Location>` on every access, and
`GetMembers()` allocates its public-model array per call.

The one Tangent-side amplifier is that the location is computed **eagerly**:

```csharp
// src/Tangent.Analyzers/Internal/RuleEvaluator.cs:249
private void EvaluateMemberRule(AppliedRule applied, in Rule rule, ISymbol member, MemberKind kind)
{
    Location location = SymbolFacts.LocationOf(member);   // ← before we know whether the rule holds
    switch (rule.Predicate.Kind) { ... }
```

so a member that satisfies all of its rules still pays one `Locations` access per rule. The same pattern
appears at the type level in `EvaluateTypeRule` (`RuleEvaluator.cs:74`, `:104`), and `Type.GetMembers()`
is re-read at `RuleEvaluator.cs:128`, `:184` and `:229` instead of being read once.

Making this lazy means threading the symbol (rather than a `Location`) through `EvaluateModifier`,
`EvaluateAttributes`, `EvaluateReturns`, `EvaluateInjects` and `EvaluateAccessor` and resolving the
location only inside `Report`. That is a real refactor of five call sites, not a one-line change, so it
has deliberately **not** been made here. It would remove roughly 400 of the 504 B, i.e. about 80 % of the
steady-state allocation for a conforming type.

### The violating path

`BoundViolatingType` allocates 22 KB for a controller that breaks six rules — message composition,
property bags, `ToDisplayString()` on the actual types, and the `Diagnostic` objects themselves. This is
the path that only runs when something is already wrong and a human is about to read the output, so the
cost is appropriate; it is measured so that a regression that starts building messages on the *passing*
path shows up immediately.

## Not implemented

**`EndToEndBenchmark`** (`tangent check` over `samples/Orders` through the CLI) is specified by §12.14 but
is **not** in this harness: `src/Tangent.Cli` contains only a `.csproj` at the time of writing, with no
`Program.RunAsync` to call. `FullCompilationAnalysis` is the closest available stand-in. Add the
benchmark once the CLI has an entry point, with a `[Config]` that keeps it out of the default sweep.

## CI

§14 asks for these to run on a fixed-size runner with the results committed, and §12.14 for a build
failure on a >10 % regression. Two rules are worth hard-asserting rather than eyeballing, because they are
contracts rather than measurements:

1. `AnalyzerBenchmarks.UnboundTypeProbe` must report **0 B**.
2. Every row of `GlobBenchmarks` and `TypePatternBenchmarks` must report **0 B**.
