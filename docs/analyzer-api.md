# `Tangent.Analyzers` public API

`Tangent.Analyzers` is the single rule engine. The compiler, the IDE, `tangent check`, `tangent diff` and
`tangent graph` all run the same `TangentAnalyzer` over the same IR from `Tangent.Core`; nothing
re-implements a predicate.

- Target: `netstandard2.0` (what the compiler loads), Roslyn floor **4.8.0**.
- `EnforceExtendedAnalyzerRules` is on: no file IO, no banned APIs, no `Environment`/`Process`/`Console`.
- Package layout: `analyzers/dotnet/cs/{Tangent.Analyzers,Tangent.Core,Tangent.Generators}.dll` plus
  `build/Tangent.Analyzers.props`, which adds `.tangent/**/*.tan` as `AdditionalFiles` with
  `TangentContract="true"`.

---

## 1. Surface

```csharp
namespace Tangent.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TangentAnalyzer : DiagnosticAnalyzer
{
    public TangentAnalyzer();                                        // compiler / IDE
    public TangentAnalyzer(IReferenceSink? referenceSink, bool includeUnbound); // CLI graph export
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }
}

public interface IReferenceSink
{
    void OnTypeBound(INamedTypeSymbol type, ImmutableArray<string> contractNames, int? layerIndex, string? layerName);
    void OnReference(INamedTypeSymbol from, INamedTypeSymbol to, ReferenceKind kind, Location location, bool violatesLayering);
}

public enum ReferenceKind : byte
{
    BaseType, Interface, Attribute, MemberSignature, Invocation, MemberAccess, ObjectCreation, TypeOf, Other
}

public static class TangentDiagnostics
{
    public const string Category = "Tangent";
    public static ImmutableArray<DiagnosticDescriptor> All { get; }
    public static DiagnosticDescriptor Get(string code);        // throws on an unknown code
    public static DiagnosticDescriptor? TryGet(string? code);   // null on an unknown code
    public static DiagnosticSeverity ToRoslyn(Tangent.Core.Diagnostics.DiagnosticSeverity severity);
}

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(TangentCodeFixProvider)), Shared]
public sealed class TangentCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; }
    public override FixAllProvider GetFixAllProvider();   // WellKnownFixAllProviders.BatchFixer
}

public static class ContractFiles
{
    public const string MetadataKey = "build_metadata.AdditionalFiles.TangentContract";
    public const string Extension = ".tan";

    public static bool IsContractFile(AdditionalText file, AnalyzerConfigOptionsProvider options);
    public static string ComputeCacheKey(ImmutableArray<AdditionalText> files);
    public static Tangent.Core.Binding.BindResult LoadModel(ImmutableArray<AdditionalText> files);
}
```

Every descriptor in `TangentDiagnostics.All` is built from `Tangent.Core.Diagnostics.DiagnosticCatalog`,
so ids, titles and default severities live in exactly one place. `helpLinkUri` is
`https://github.com/caybokotze/tangent/blob/main/docs/diagnostics.md#tngNNNN`.

The Roslyn `messageFormat` is a single `{0}` hole: the analyzer composes the whole message (contract,
member, expectation, actual, rule id and `.tan` location) and passes it as the one argument. The Core
template is still what shapes the text; `description` on each descriptor carries that raw template.

---

## 2. Diagnostic properties

Every diagnostic carries a property bag so the CLI, the graph exporter and the code fixes never parse a
message:

| Key | Present on | Value |
|---|---|---|
| `contract` | all | The contract name, or `""` for layer, model and binding diagnostics. |
| `ruleId` | all | The stable `Rule.Id` (`Controller/ctor/injects-only#0`), `layer:From->To` for TNG0202, `""` for model diagnostics. |
| `member` | all | The member's name, or `""` for type-level and model diagnostics. |
| `tier` | all | `symbol`, `reference`, `flow`, or `binding` / `ignore` / `model` for the non-rule diagnostics. |
| `fix` | when a mechanical fix exists | One of the fix kinds below. Absent means "no fix". |
| `fixArg` | with most fix kinds | The fix's argument: a modifier list, a type name, or an attribute specification. |
| `fromLayer`, `toLayer`, `symbol` | TNG0202 only | The layer names and the referenced symbol's name. |

Message shape: `{Contract}: {member} {expected}, found {actual}. (rule {RuleId}, {file}:{line})`, e.g.

```
Controller: method 'GetSummaries' must return Task<ActionResult<*>> | Task<IActionResult>,
found System.Collections.Generic.IEnumerable<ReportViewModel>. (rule Controller/methods/returns#0, controllers.tan:17)
```

---

## 3. Fix kinds

| `fix` | `fixArg` | Produced by | What it does |
|---|---|---|---|
| `add-modifier` | modifier list, e.g. `sealed`, `private readonly` | TNG0101 (require), TNG0303 | Adds each modifier; adding an accessibility keyword replaces the existing one. |
| `remove-modifier` | modifier list | TNG0101 (forbid), TNG0108 | Removes each modifier; removing an accessibility keyword replaces it with `private`. |
| `add-attribute` | `Name` or `Name(*)` | TNG0104 (require) | Inserts `[Name]` as the first attribute list, adding the declaring namespace's `using` when needed. `(*)` emits arguments: `[ProducesResponseType(StatusCodes.Status200OK)]` when `Microsoft.AspNetCore.Http.StatusCodes` resolves, `[ProducesResponseType(200)]` when it does not, `("")` for anything else. |
| `add-base-type` | simple type name | TNG0102 (require) | Adds or replaces the base class, adding a `using` when needed. |
| `add-interface` | simple type name (templates already expanded) | TNG0103 (require) | Appends the interface to the base list, adding a `using` when needed. |
| `setter-to-init` | — | TNG0107 (`forbid public set`) | Rewrites `set` to `init` on C# 9+, otherwise prefixes the accessor with `private`. |
| `remove-using` | — | *not currently produced* | Removes the enclosing `using` directive. Registered so a future syntax-level `forbid using` check can use it. |

`GetFixAllProvider()` returns `WellKnownFixAllProviders.BatchFixer`, so `dotnet format analyzers` and
`tangent diff --apply` can apply the whole set in one pass. Tier 1 (reference/layer) and Tier 2 (flow)
diagnostics deliberately carry no `fix`.

---

## 4. Hosting the analyzer from the CLI

```csharp
// 1. The contract files. They must be AdditionalTexts whose Path ends in .tan, or that carry
//    TangentContract=true metadata, so ContractFiles.IsContractFile accepts them.
ImmutableArray<AdditionalText> contracts = Directory
    .EnumerateFiles(tangentFolder, "*.tan", SearchOption.AllDirectories)
    .Select(path => (AdditionalText)new FileAdditionalText(path))   // your own AdditionalText
    .ToImmutableArray();

// 2. The options. AnalyzerOptions(additionalFiles) is enough: without an
//    AnalyzerConfigOptionsProvider the .tan extension alone identifies the files.
var options = new AnalyzerOptions(contracts);

// 3. The analyzer, with or without a graph sink.
var analyzer = new TangentAnalyzer(sink, includeUnbound);

// 4. Run it.
CompilationWithAnalyzers withAnalyzers = compilation.WithAnalyzers(
    ImmutableArray.Create<DiagnosticAnalyzer>(analyzer),
    options,
    cancellationToken);

ImmutableArray<Diagnostic> diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync(cancellationToken);
```

Notes:

- When the CLI drives an `MSBuildWorkspace`, add the `.tan` files as `AdditionalDocument`s on each
  project (`project.AddAdditionalDocument(name, text, filePath: path)`) and use the
  `AnalyzerOptions` Roslyn builds for that project; `ContractFiles.IsContractFile` accepts them by
  extension, exactly as the `build/Tangent.Analyzers.props` path does in a normal build.
- `GetAnalyzerDiagnosticsAsync` returns **hidden** diagnostics too, so TNG9000 notes for ignored violations are
  available for `tangent check --show-ignored` without re-running anything.
- Use `ContractFiles.ComputeCacheKey` if the CLI wants to know whether two projects share a model; the
  analyzer already caches by that key through `Tangent.Core.ModelCache`, process-wide.
- One `TangentAnalyzer` instance may be reused across every project in the solution. Per-compilation
  state is created in `CompilationStartAction` and never shared.

### The reference sink

When the analyzer is constructed with a sink:

- `OnTypeBound` fires **once per named type** from the symbol action: for every type bound to at least one
  contract, and — when `includeUnbound` is true — for every other named type as well. `contractNames` is
  empty for an unbound type; `layerIndex`/`layerName` are `null` when the namespace is outside every layer.
- `OnReference` fires for every type-to-type reference the Tier 1 walk observes, from both halves of the
  walk:
  - the **symbol** half: base type, interfaces, attributes, and member signatures (return types,
    parameters, field/property/event types), with `ReferenceKind.BaseType`, `Interface`, `Attribute` or
    `MemberSignature`; the types named in a **type-level** attribute's arguments (`[Model(typeof(X))]`)
    are reported as `TypeOf` here, because a type's attribute arguments reach no operation block;
  - the **operation** half: invocations, field/property/event/method references, object creation and
    `typeof`, with `Invocation`, `MemberAccess`, `ObjectCreation` or `TypeOf`; plus every other written
    type reference in the body — `is` type tests and type/declaration/recursive patterns, explicit casts
    and `as`, explicitly typed local and `foreach` declarations, array creation, `default(T)`, `sizeof`
    and the type arguments a generic method was called with — with `Other` (or the fitting kind).
    A `var` local is not reported: it writes no type name, and its initializer already carries the
    reference.
- `to` is always the referenced type's `OriginalDefinition`. Generic arguments are reported as separate
  references (`Task<OrderDto>` yields both `Task` and `OrderDto`) up to four levels deep.
- `violatesLayering` is `true` when the layer matrix forbids the reference. It is computed even when no
  diagnostic is reported for it (a repeated pair is reported once per member but handed to the sink every
  time).
- Self-references, references to the global namespace, type parameters and `dynamic` are skipped.
- The sink is called from concurrent analyzer callbacks. **It must be thread-safe.**

---

## 5. What the analyzer does, in order

1. **`Initialize`** — `EnableConcurrentExecution()`, `ConfigureGeneratedCodeAnalysis(None)`,
   `RegisterCompilationStartAction`.
2. **Compilation start** — collect the contract files; if there are none, register nothing (zero cost for
   projects without a `.tangent` folder). Otherwise load the model through `ContractFiles.LoadModel`,
   which parses at most once per unique content. Register the compilation-end action always, and the
   symbol/operation actions only when the model bound without errors.
3. **Symbol action (`SymbolKind.NamedType`)** — bind the type (attribute → `bind` → `match`, in that
   precedence), apply ignores, then run Tier 0 and the symbol half of the Tier 1 walk.
4. **Operation block start** — build the per-body worker once, then register operation actions for the
   Tier 1 walk and an operation-block-end action for Tier 2 constructors.
5. **Compilation end** — report the model diagnostics (TNG1xxx) at their `.tan` locations, plus TNG1002
   and TNG1020.

### Binding precedence

`[Tangent("X")]` wins outright; if no attribute names a known contract, `bind` statements win; if none
match, every contract whose `match` clause holds applies. A type bound to both a contract and a contract
that extends it evaluates each inherited rule once (rules are deduplicated by `Rule.Id`).

### Performance

- The path from `AnalyzeNamedType` to "this type is bound to nothing and lives outside every layer" is
  allocation-free: the contract probe runs over a `stackalloc int[32]`, namespace names and layer indices
  are cached per `INamespaceSymbol` (`SymbolEqualityComparer.Default`), and no `TypeContext` is built.
- Type patterns are matched structurally against `INamedTypeSymbol` — name, arity, arguments, wildcards,
  alternation — walking the Core tables. No `ToDisplayString()` on a comparison path.
- `RuleEvaluator` and `DiagnosticReporter` are `ref struct`s; the operation callbacks are instance methods
  on one worker per body, not closures.
- Regexes for `name matches`, resolved type names and namespace facts are cached per compilation in
  `ConcurrentDictionary`s. No locks in any action.

---

## 6. Known limitations and deviations

1. **`stateless` is evaluated in the symbol action**, not the operation block, because it is derivable
   from symbols alone (no mutable instance fields, no settable instance auto-properties). This makes it
   work for types with no constructor body. Static state is ignored, as specified.
2. **`each parameter not stored`** has no dedicated code in Appendix A, so it is reported as **TNG0301**
   with its own message ("must not be stored in a field or property").
3. **Primary constructors have no body to walk.** `each parameter assigned to field` counts a primary
   constructor parameter only when an explicit field is declared with an initializer that names it
   (`private readonly IMediator _m = mediator;`). Implicit captures do not count, which is what the plan
   asks for. The check is syntactic (the field's `VariableDeclarator` initializer must be a plain
   identifier), so `= mediator ?? throw ...` is not currently recognised as a capture.
4. **`forbid using` produces no code fix.** The plan's §9.6 says reference violations get no fix; the
   `remove-using` kind exists in the provider but nothing emits it, because TNG0201 is reported at the
   reference site (which is far more useful) rather than at the `using` directive.
5. **Signature references and body references are deduplicated independently.** A pair
   (from-type, to-type) is reported once per member signature and once per member body, so that the result
   never depends on whether the symbol action or the operation action ran first. A field with an
   initializer that mentions the same forbidden type therefore reports twice: once on the declaration, once
   on the initializer.
6. **TNG1020 is only reported for contracts this compilation actually uses.** One `.tangent` folder is
   shared by projects that see different assemblies, so validating `inherits ApiControllerBase` inside a
   Domain project that has never heard of ASP.NET produces nothing but noise. A contract with no bound
   type in the compilation is skipped entirely.
7. **TNG1002 is suppressed when the named namespace does not exist in the compilation.** Same reason:
   `bind Orders.Api.Controllers.HealthController` is not an error in the Domain project. The CLI, which
   sees every project, is the right place to decide that a `bind` target exists nowhere.
8. **Type-name existence** (for TNG1020) is checked through `IAssemblySymbol.TypeNames` for simple names
   and `Compilation.GetTypeByMetadataName` for dotted ones, with and without the arity backtick. Nested
   types spelled with a `.` rather than a `+` may not be found.
9. **`interface I{Name} lives in`** looks for the companion interface among the type's own interfaces
   first, then among the compilation's source declarations. An interface that exists only in metadata is
   not found, and the rule then reports as if it were absent.
10. **Records' synthesised members are skipped** (`Equals`, `GetHashCode`, `ToString`, `PrintMembers`,
    `Deconstruct`, the clone method and `EqualityContract`), as are accessors, operators, implicitly
    declared members and auto-property backing fields. Positional record properties are reached only when
    Roslyn marks them as explicitly declared.
11. **`Modifier` messages substitute the member for the word "type"** in Core's template (`"{0}: type must
    be {1}"` becomes `"DomainService: field '_evaluations' must be readonly"`). The catalogue stays the
    single source of wording; only the subject changes.
12. **Model errors stop enforcement.** When `BindResult.HasErrors` is true the symbol and operation actions
    are never registered, so a broken `.tan` file produces its TNG1xxx diagnostics and nothing else.

---

## 7. `Tangent.Generators`

`ContractConstantsGenerator` is an `IIncrementalGenerator` reading the same `AdditionalFiles`
(`TangentContract=true` metadata or a `.tan` extension) and emitting `TangentContracts.g.cs`:

```csharp
// <auto-generated/>
#nullable enable

namespace Tangent
{
    public static class Contracts
    {
        public const string Controller = "Controller";

        public const string Repository = "Repository";
    }
}
```

Contract names are sorted ordinally and emitted once each. The generator emits nothing when there are no
contract files or no contracts, and reports no diagnostics of its own — the analyzer's TNG0001 remains the
source of truth for a misspelt contract name.

---

## 8. Tests

`tests/Tangent.Analyzers.Tests` (103 tests) uses `CSharpAnalyzerTest<TangentAnalyzer, DefaultVerifier>` and
`CSharpCodeFixTest<TangentAnalyzer, TangentCodeFixProvider, DefaultVerifier>` against
`ReferenceAssemblies.Net.Net80`. Every test supplies its contracts as an additional file at
`.tangent/contracts.tan` and compiles against `Stubs.cs`, which declares the two Tangent attributes inline
and a miniature ASP.NET-like framework, so the tests depend on no other Tangent assembly than the one
under test.
