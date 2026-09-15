# Getting started with Tangent

This guide takes an existing .NET solution from zero to enforced architecture in four steps.

## 1. Install

```bash
# In every project (or once in Directory.Build.props):
dotnet add package Tangent.Attributes
dotnet add package Tangent.Analyzers

# The CLI, as a local tool:
dotnet new tool-manifest
dotnet tool install Tangent.Cli
```

`Tangent.Analyzers` is a development dependency. It ships a `.props` file that finds the nearest ancestor folder containing `.tangent/` and hands every `*.tan` file in it to the compiler as an additional file. Nothing about Tangent ends up in your built assemblies: the attributes are `[Conditional("TANGENT_ATTRIBUTES")]` and are stripped unless you define that symbol.

## 2. Describe the shape

```bash
dotnet tangent init
```

This scaffolds `.tangent/project.tan` with one layer per project root namespace and no allowed dependencies. Edit it to declare the direction dependencies may flow:

```tangent
project "Scheduling" {
    layer Api            = Scheduling.Api.**
    layer Application    = Scheduling.Application.**
    layer Domain         = Scheduling.Domain.**
    layer Infrastructure = Scheduling.Infrastructure.**

    Api            -> Application, Domain
    Application    -> Domain
    Infrastructure -> Application, Domain
    Domain         -> nothing
}
```

`System.**` is always allowed. Add `allow Microsoft.Extensions.**` inside the project block for other framework namespaces.

Then add contracts, one file per concern. A contract has a `match` clause (convention binding), rules, and optional member blocks:

```tangent
contract Repository {
    match: class in Infrastructure where name ends with "Repository"

    implements I{Name}
    interface I{Name} lives in Domain
    sealed

    ctor { injects only SchedulingDbContext }
    methods where public { returns Task<*> }

    forbid using Scheduling.Api.**
}
```

Two keywords carry all constraint semantics: `require` (the default for a bare predicate) and `forbid`. See [TANGENT-PLAN.md §5](../TANGENT-PLAN.md) for the full grammar and [diagnostics.md](diagnostics.md) for every code.

## 3. Bind and exempt

Three binding sources, in precedence order:

1. `[Tangent("Repository")]` on the type. With the source generator you can write `[Tangent(Contracts.Repository)]` and get a compile error on a typo.
2. `bind Scheduling.Infrastructure.LegacyRepository to Repository except [ProducesResponseType]` in a `.tan` file.
3. The contract's `match` clause.

Escape hatches are visible, never silent:

```tangent
exempt Scheduling.Infrastructure.Repositories.LegacyReportRepository reason "migrating in Q4 2026"
exempt Scheduling.Domain.Appointments.AuditEntry from Entity reason "append-only log"
```

```csharp
[TangentExempt("export streams bytes", Contract = "Controller")]
public async Task<IActionResult> Export() { ... }
```

`dotnet tangent check --show-exemptions` lists every applied exemption with its reason.

## 4. Enforce

Locally, the analyzer runs on every build and in the IDE:

```
warning TNG0104: Controller: method GetById must have attribute [ProducesResponseType(*)] ...
warning TNG0202: Layer Api may not depend on layer Infrastructure (InMemoryAppointmentRepository) ...
```

Escalate in `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.TNG0202.severity = error
```

In CI, either gate on the compiler. MSBuild takes exact codes only, no wildcards:

```bash
dotnet build -warnaserror:TNG0202,TNG0201,TNG0203
```

To make every Tangent diagnostic an error at once, use the analyzer category in `.editorconfig` or a `.globalconfig`:

```ini
[*.cs]
dotnet_analyzer_diagnostic.category-Tangent.severity = error
```

Or use the CLI for SARIF and a summary:

```bash
dotnet tangent check --sarif tangent.sarif --fail-on warning
```

Exit codes: 0 clean, 1 violations at or above `--fail-on`, 2 model errors in `.tan` files, 3 load failure.

## Fixing violations

Mechanical Tier 0 violations (missing attribute, missing base type, `sealed`, `readonly`, setter to `init`) have code fixes. They appear as lightbulbs in the IDE, and the CLI can show or apply them in bulk:

```bash
dotnet tangent diff          # unified diff of every applicable fix
dotnet tangent diff --apply  # write the changes
```

Reference and flow violations have no mechanical fix; they are design decisions.

## Seeing the architecture

```bash
dotnet tangent graph -o tangent.graph.json --include-unbound
```

Load the file in the viewer (`cd viewer && npm run dev`, then use the file picker or `?graph=<url>`). Layers are swimlanes ordered by dependency direction, nodes are coloured by contract, and edges that violate layering are red.

## Editing `.tan` files

The VS Code extension in `editors/vscode` provides highlighting, diagnostics, completion, hover with the list of bound types, go-to-definition into C#, and find-references. It starts `dotnet tangent serve` over stdio. JetBrains IDEs can load the same TextMate grammar.

## Worked examples

- `samples/Orders`: a clean-architecture solution with 36 documented deliberate violations. Used by the end-to-end tests.
- `demo/Scheduling`: a runnable Scheduling API that consumes the packaged analyzer and CLI, with 6 documented violations and a full walkthrough in its README.
