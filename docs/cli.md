# `dotnet tangent`

The command-line front end. It applies the same `TangentAnalyzer` the compiler and the IDE run, over the
same IR `Tangent.Core` binds, so `tangent check` and `dotnet build` agree by construction — there is
exactly one rule engine.

```
dotnet tool install --global Tangent.Cli
tangent check
```

---

## 1. Commands

```
tangent init  [path] [--force]
tangent lint  [path] [--strict] [--dump-ir] [--dump-ast]
tangent check [path] [--sarif out.sarif] [--json out.json] [--fail-on warning|error]
                     [--show-exemptions] [--explain] [--project name]... [--strict]
tangent diff  [path] [--apply] [--project name]...
tangent graph [path] [-o tangent.graph.json] [--include-unbound] [--include-source]
                     [--project name]...
tangent serve [--workspace path]
```

`--no-color` and `--verbose` work on every command.

### `path`

Defaults to the current directory. It may be:

| Given | Meaning |
|---|---|
| a `.sln` or `.slnx` file | that solution |
| a `.csproj` file | that project alone |
| a directory | the single solution in it; failing that, the single project in it |

A directory holding no solution and no project, or more than one of either, is a **load failure**
(exit 3) with a message naming what it found.

The `.tangent` folder is searched for at the solution's (or project's) directory and then in every
ancestor, so a monorepo can keep one `.tangent` at the root and check a project deep inside it. Every
`*.tan` file under that folder, recursively, is part of the model.

Paths in every report are relative to the **solution root** — the directory the `.sln`/`.slnx`/`.csproj`
lives in.

---

## 2. `tangent init`

Scaffolds a starting `.tangent/` folder:

- `project.tan` — one `layer` per distinct **root namespace** across the solution's projects, every one
  of them at `-> nothing`, plus a commented-out example contract.
- `contracts.tan` — empty, ready for the first real contract.

The root namespace comes from each project file's `<RootNamespace>`, falling back to the file name, which
is what MSBuild itself defaults to. `init` reads the project files directly instead of loading MSBuild, so
it is instant and works on a solution that has never been restored. A layer is named after the last
segment of its namespace when that is unique (`Shop.Api` → `Api`) and after the whole namespace with the
dots removed when it is not.

```
$ tangent init
Created .tangent\project.tan with 4 layers.
Created .tangent\contracts.tan.

Next: open project.tan, say which layers may depend on which, then run 'tangent check'.
```

`init` **refuses to overwrite** an existing `project.tan` or `contracts.tan`; `--force` overwrites both.

Starting every layer at `-> nothing` is deliberate. The first `tangent check` then shows you the whole
dependency graph as violations, and you open the direction up one `->` line at a time, deciding each one
on purpose.

| Exit | When |
|---|---|
| 0 | the folder was written |
| 3 | the path did not resolve, the folder exists and `--force` was not given, or the solution lists no projects |

---

## 3. `tangent lint`

Everything that can be said about the `.tan` files themselves: lexing, parsing, binding, conflicting
rules, contracts that can never bind. **No compilation is needed**, so pointing it straight at a
`.tangent` folder is the fast path:

```
$ tangent lint samples/Orders/.tangent
note: no solution or project was found, so contract type names were not resolved (TNG1020 skipped).

7 contracts, 4 layers, 6 files — 0 errors, 0 warnings
```

When the path does resolve to a solution or project, `lint` additionally loads it and checks that every
C# type name the contracts mention (`inherits ApiControllerBase`, `injects only IMediator`, `forbid new
DbContext`, …) exists in **some** compilation, reporting **TNG1020** for the ones that exist nowhere.
This is the CLI's job rather than the analyzer's: one `.tangent` folder is shared by projects that see
different assemblies, so only the tool that sees every project at once can say a name is genuinely wrong.

| Option | Effect |
|---|---|
| `--dump-ir` | print the bound model as JSON (`IrJsonWriter`) — the same text the Core golden tests compare |
| `--dump-ast` | print the tokens and AST of every `.tan` file (`SyntaxTreePrinter`) |
| `--strict` | treat warnings, TNG1020 included, as errors |

| Exit | When |
|---|---|
| 0 | no errors |
| 2 | at least one error — with `--strict`, that includes every warning |
| 3 | no `.tangent` folder, or it holds no `.tan` files |

---

## 4. `tangent check`

The command CI runs.

```
$ tangent check samples/Orders
Binding
  error TNG0001 src/Orders.Api/Controllers/LegacyOrdersController.cs:16:2  Contract 'Controler' is not defined. Did you mean 'Controller'?

Controller
  warning TNG0101 src/Orders.Api/Controllers/CustomersController.cs:15:14  Controller: type must be sealed. (rule Controller/type/modifier#0, controllers.tan:7)
  warning TNG0104 src/Orders.Api/Controllers/CustomersController.cs:28:56  Controller: method 'GetByIdAsync' must have attribute [ProducesResponseType(*)]. (rule Controller/methods/attributes#1, controllers.tan:19)
  ...

Layers
  warning TNG0202 src/Orders.Api/Controllers/ReportsController.cs:20:38  Layer Api may not depend on layer Infrastructure (OrdersDbContext). (rule layer:Api->Infrastructure, project.tan:7)
  ...

59 violations (1 error, 58 warnings) in 5 projects, 4 exemptions applied
```

Findings are grouped by **contract**, and within a group ordered by file, line and column. Four headings
belong to no contract:

| Heading | What it collects |
|---|---|
| `Model` | TNG1xxx — problems in the `.tan` files themselves |
| `Binding` | TNG000x — a `[Tangent("X")]` naming a contract that does not exist, an exemption naming a rule that does not |
| `Layers` | TNG0202 — the layer dependency matrix |
| *contract name* | every rule violation of that contract |

Each line is `  {severity} {code} {file}:{line}:{col}  {message}`. Colour is plain ANSI and is switched
off by `--no-color`, by `NO_COLOR` being set to anything, or by stdout being redirected.

### Options

| Option | Effect |
|---|---|
| `--sarif <file>` | also write a SARIF 2.1.0 log |
| `--json <file>` | also write the JSON report |
| `--fail-on warning\|error` | the lowest severity that makes the command fail. **Default `error`, so warnings alone exit 0** |
| `--strict` | promote every warning to an error, in the console, in both report files, and in the exit code |
| `--show-exemptions` | list the TNG9000 notes for exemptions that suppressed a violation, with their reason, and include them in the report files |
| `--explain` | follow each violation with the rule text, where it is declared, what applies it, and the offending line. See [below](#--explain) |
| `--project <name>` | restrict the run to this project. Repeatable |

### `--explain`

A rule id is a lookup key for a person. The rule **text** is what a person, or an agent in a fix loop,
can act on directly. `--explain` adds it under every violation that belongs to a rule:

```
$ tangent check samples/Orders --explain
Controller
  warning TNG0101 src/Orders.Api/Controllers/CustomersController.cs:15:14  Controller: type must be sealed. (rule Controller/type/modifier#0, controllers.tan:7)
      rule        require sealed
      declared    .tangent/controllers.tan:7
      applies to  contract Controller matches class in Api where name ends with "Controller"
      15 | public class CustomersController : ApiControllerBase
         |              ^^^^^^^^^^^^^^^^^^^
  warning TNG0203 src/Orders.Api/Controllers/LegacyOrdersController.cs:28:26  Controller: forbidden object creation: new HttpClient. (rule Controller/methods/new#1, controllers.tan:20)
      rule        methods where public { forbid new HttpClient }
      ...

Layers
  warning TNG0202 src/Orders.Api/Controllers/LegacyOrdersController.cs:32:24  Layer Api may not depend on layer Infrastructure (OrdersDbContext). (rule layer:Api->Infrastructure, project.tan:7)
      rule        Api -> Application, Domain
      declared    .tangent/project.tan
      applies to  layer Api = Orders.Api.**
      ...
```

| Line | What it shows |
|---|---|
| `rule` | the rule rendered back to DSL from the bound model, inside the member block or `where { }` refinement that scopes it. For a layer violation, the layer's `->` line; for TNG0205, the effective `allow` list |
| `declared` | the `.tan` file and line of the rule. Layer rules show the file only, because the `->` lines carry no position of their own |
| `applies to` | the contract's `match` clause, or the `layer` line whose glob puts the type in that layer. Absent for a contract with no `match` (bound by `bind` or `[Tangent]`) |
| the code | the offending line, with a caret under the reported span |

Model and binding diagnostics (TNG1xxx, TNG0001) belong to no rule and are printed without an
explanation. `--explain` changes no finding and no exit code.

The report files carry the same information. Each JSON row gains an `explain` object with `rule`,
`declared`, `appliesTo` and `source`. Each SARIF result's `properties` gains `ruleText`, `ruleDeclared` and
`appliesTo`. Without `--explain`, neither file changes.

### Exit codes

| Exit | When |
|---|---|
| 0 | nothing at or above `--fail-on` |
| 1 | violations at or above `--fail-on` |
| 2 | the model has errors (a TNG1xxx error) — no enforcement happened, so fix the `.tan` files first |
| 3 | the solution, project or `.tangent` folder could not be loaded |

`--fail-on error` is the default and means exactly what it says: a run whose only findings are warnings
exits **0**. Reach for `--fail-on warning` (or `--strict`) once a codebase is clean and you want to keep
it that way.

Note that a few diagnostics are errors by default — **TNG0001** (a `[Tangent("…")]` naming a contract
that does not exist) is one — so a solution with a misspelt contract name fails even at `--fail-on error`.
That is the case in `samples/Orders`, which is why `tangent check samples/Orders` exits 1.

### Compiler errors

Before analysing, the CLI collects the C# compiler's own errors. If there are any it prints a warning —
analysis of a broken compilation is unreliable, because unresolved types make rules pass that should fail —
but it does **not** fail the run. `--verbose` lists them.

### JSON report

A plain array, sorted by file, line, column and code so two runs diff cleanly:

```json
[
  {
    "code": "TNG0101",
    "severity": "warning",
    "message": "Controller: type must be sealed. (rule Controller/type/modifier#0, controllers.tan:7)",
    "file": "src/Orders.Api/Controllers/CustomersController.cs",
    "line": 15,
    "col": 14,
    "endLine": 15,
    "endCol": 33,
    "contract": "Controller",
    "ruleId": "Controller/type/modifier#0",
    "member": "",
    "project": "Orders.Api"
  }
]
```

### SARIF

SARIF 2.1.0, hand-written — Tangent takes no dependency on the SARIF SDK. `tool.driver` carries every
descriptor in `TangentDiagnostics.All` as a rule, with its `shortDescription`, `defaultConfiguration.level`
and `helpUri`. Results are located relative to the solution root through the `SRCROOT`
`originalUriBaseIds` entry, and each result's `properties` is the analyzer's own property bag (`contract`,
`ruleId`, `member`, `tier`, `fix`, `fixArg`, …) plus the project name.

Severity maps to SARIF levels as `error → error`, `warning → warning`, everything else (`info`, `hidden`)
`→ note`; hidden results additionally carry `"kind": "informational"`.

---

## 5. `tangent diff`

Prints, as a unified diff with three lines of context, what the mechanical code fixes would do. Only Tier
0 (structural) violations have fixes — a reference or flow violation has no mechanical answer and
deliberately carries none.

```
$ tangent diff samples/Orders
--- a/src/Orders.Api/Controllers/CustomersController.cs
+++ b/src/Orders.Api/Controllers/CustomersController.cs
@@ -12,7 +12,7 @@
 [ApiController]
 [Route("api/customers")]
 // VIOLATION (TNG0101): the Controller contract requires `sealed`.
-public class CustomersController : ApiControllerBase
+public sealed class CustomersController : ApiControllerBase
 {
     private readonly IMediator _mediator;
     private readonly ILogger<CustomersController> _logger;

12 fixes in 9 files. Re-run with --apply to write them.
```

`--apply` writes the changes through the MSBuild workspace and names each file:

```
$ tangent diff samples/Orders --apply
wrote src/Orders.Api/Controllers/CustomersController.cs (2 fixes)
...
```

| Exit | When |
|---|---|
| 0 | nothing to change, or `--apply` wrote everything |
| 1 | there are fixes to make and `--apply` was not given (so `tangent diff` works in CI like `git diff --exit-code`) |
| 3 | load failure, or the workspace refused the changes |

The fixes for one file are applied one at a time to the document as it stands, and the spans of the fixes
still to come are shifted through the text changes each edit makes. The analyzer is **not** re-run.

---

## 6. `tangent graph`

Exports `tangent.graph.json` — the document `viewer/` reads. The exporter is an `IReferenceSink` the
analyzer feeds during its own Tier 1 walk, so the graph costs one extra dictionary per reference rather
than a second pass over the solution.

```
$ tangent graph samples/Orders --include-unbound
Wrote samples/Orders/tangent.graph.json
  4 layers, 7 contracts, 62 nodes, 152 edges, 59 violations
  49 C# files: 18 tracked, 6 partial, 25 untracked
```

| Option | Effect |
|---|---|
| `-o`, `--output <file>` | where to write. Default `<solution root>/tangent.graph.json` |
| `--include-unbound` | add the types no contract binds, so the graph shows the whole solution |
| `--include-source` | embed the code around each violation (15 lines either side), the full text of every `.tan` file, and the absolute solution root for `vscode://file/` links |
| `--project <name>` | restrict the run to this project. Repeatable |

The schema is TANGENT-PLAN.md §11.1 and `viewer/src/graph/types.ts`: `version` (currently 2), `project`,
`generatedAt`, `layers[]`, `contracts[]`, `files[]`, `nodes[]` (with `members[]`), `edges[]`,
`violations[]`, and the optional `rules[]` array carrying each rule's `.tan` text so the viewer can show
it beside a violation. Node ids
are fully qualified metadata names (`Orders.Api.Controllers.OrdersController`, with a `` `N `` arity
suffix for generic types). Everything is ordered deterministically, so two runs over unchanged sources
differ only in `generatedAt`.

To open the export in the viewer:

```
cd viewer && npm run dev      # then http://localhost:5173/?graph=/orders.graph.json
```

**Without `--include-unbound` the graph holds only the types a contract binds.** Edges whose other end is
outside the graph — the BCL, a NuGet package, or an unbound type — are dropped rather than left dangling,
and in a codebase like `samples/Orders` the types that break layering (`OrdersDbContext`, `CacheKeys`,
`INotificationDispatcher`) are bound to nothing. Pass `--include-unbound` to see the illegal edges.
`violations[]` always carries every violation of the run, whether or not its type is in `nodes[]`.

### The file table

`files[]` lists every `.tan` file and every hand-written C# file of the analysed projects. Generated
files (`obj/`, `bin/`, `*.g.cs`) and files outside the solution root are left out. Each entry has an
integer `id` (its index; the table is sorted by path), and `nodes[]` and `violations[]` point into it
with `fileId` (-1 for a type declared outside the root). The viewer addresses files by id only, so no
path parameter ever has to be accepted (TANGENT-PLAN.md §11.4).

| `status` | Meaning |
|---|---|
| `tracked` | every type the file declares is bound to a contract |
| `partial` | some of its types are bound; typically a command bound as nothing next to its bound handler |
| `untracked` | none of its types are bound, or it declares no type (`Program.cs`) |
| `contract` | a `.tan` file |

Coverage is independent of `--include-unbound`: the file table is the same either way. Note that
`untracked` means *no contract*. Layer rules still apply to every type whose namespace falls in a layer.

**`--include-source` makes the file machine-specific** (it records the absolute root) and larger, so
keep it for local use and leave it off graphs you commit.

---

## 7. `tangent serve`

Runs the `.tan` language server on stdio (`TangentLanguageServerHost.RunAsync`). `--workspace <path>`
sets the workspace root; without it the server takes the root from the client's `initialize` request.

Nothing but LSP traffic is written to standard output in this mode. Errors go to standard error.

This is what `editors/vscode` starts.

---

## 8. Running Tangent in CI

There are two routes, and they produce the same diagnostics because they run the same analyzer.

### The CLI, with SARIF for code scanning

```yaml
- uses: actions/setup-dotnet@v4
  with: { dotnet-version: '10.0.x' }

- run: dotnet tool install --global Tangent.Cli
- run: dotnet restore
- run: tangent check --sarif tangent.sarif --fail-on error

- uses: github/codeql-action/upload-sarif@v3
  if: always()
  with:
    sarif_file: tangent.sarif
```

`dotnet restore` first: the CLI opens the solution with MSBuild and a project that has not been restored
will not load. `if: always()` uploads the log even when the step failed, which is the point of it.

### The analyzer package, with no CLI at all

Reference `Tangent.Analyzers` and let the compiler do the work:

```yaml
- run: dotnet build -warnaserror:TNG0101,TNG0102,TNG0104 -p:ErrorLog=tangent.sarif%3Bversion=2.1
```

or, to make every Tangent diagnostic fatal, put it in the build itself:

```xml
<PropertyGroup>
  <WarningsAsErrors>$(WarningsAsErrors);TNG0101;TNG0102;TNG0103;TNG0104;TNG0105;TNG0106;TNG0107;TNG0108;TNG0109;TNG0201;TNG0202;TNG0203;TNG0301;TNG0302;TNG0303</WarningsAsErrors>
</PropertyGroup>
```

This route needs no extra tool, costs nothing beyond the build you already run, and gives every developer
the same squiggles in the IDE. Its limits are the ones the CLI exists to lift: no graph export, no
cross-project TNG1020 resolution, and no single report over the whole solution.

Choose per severity in `.editorconfig` when a blanket rule is too blunt:

```ini
[*.cs]
dotnet_diagnostic.TNG0202.severity = error
dotnet_diagnostic.TNG0104.severity = suggestion
```

---

## 9. Performance

The MSBuild workspace load dominates and is outside Tangent's control — seconds, not milliseconds. What
the CLI does about it:

- Projects are compiled and analysed **in parallel** (`Parallel.ForEachAsync`).
- One `TangentAnalyzer` instance serves the whole solution; per-compilation state is built in
  `CompilationStartAction` and never shared.
- The `.tan` model is parsed **once** per unique content, through `ContractFiles.LoadModel` and the
  process-wide `ModelCache`, however many projects share the folder.
- `check`, `graph` and `diff` each cost exactly one workspace load and one analyzer pass; the graph is a
  sink on that same pass.
- `--project` restricts the run to what you care about.
- `tangent lint` pointed at a `.tangent` folder loads no workspace at all.

Diagnostics are deduplicated across projects by `(id, file, span, message)`, because linked files, shared
source and multi-targeting otherwise report the same thing several times.

---

## 10. Known limitations

1. **One fix per declaration per `tangent diff` run.** Fixes are applied sequentially without re-running
   the analyzer, so when two violations target the *same* declaration — `require private` and
   `require readonly` on one field, say — the first is applied and the second is skipped, because the
   edit rewrote the span it pointed at. Re-running `tangent diff --apply` converges. Fixes at different
   places in the same file all apply in one run.
2. **`tangent diff` re-indents nothing.** The code fixes preserve the surrounding trivia; they do not run
   a formatter over the file.
3. **`tangent init` does not read MSBuild.** It parses the solution and the project files for
   `<RootNamespace>`. A root namespace that comes from a property, an import or a `Directory.Build.props`
   is not seen, and the project file name is used instead.
4. **`--include-unbound` is needed to see layering violations in the graph.** See §6.
5. **The graph counts no external references.** Edges to types outside the solution are dropped, not
   totalled on the node.
6. **`tangent check` needs a restored solution.** MSBuild cannot load a project without its
   `project.assets.json`. Run `dotnet restore` first; `--verbose` shows the MSBuild diagnostics when a
   project fails to load.
7. **Analysis on a broken compilation is unreliable.** The CLI warns and carries on rather than failing,
   because a partial answer is usually still useful — but treat it with suspicion.
8. **`--project` matches on project name, not path**, and is case-insensitive. A solution with two
   projects of the same name in different folders selects both.
9. **The edge `kind` is always `reference`.** Edges aggregate every kind of reference between a pair of
   types (base type, interface, attribute, signature, invocation, `new`, `typeof`), and the aggregate has
   no single kind.
