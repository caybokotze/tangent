# AGENTS.md

Context for coding agents working in this repository.

## Project

Tangent is an architectural conformance checker for .NET. Contracts are written in a small DSL (`.tan` files in a `.tangent/` folder) and checked by a Roslyn analyzer, a CLI, and a language server. `TANGENT-PLAN.md` is the design document and roadmap; read it before large changes.

## Layout

- `src/Tangent.Core` — lexer, parser, binder, rule IR. Roslyn-free.
- `src/Tangent.Analyzers` — Roslyn analyzer and code fixes. Also packs Core and Generators.
- `src/Tangent.Generators` — optional contract constants generator.
- `src/Tangent.Cli` — `tangent` tool: `init`, `lint`, `check`, `diff`, `graph`, `serve`.
- `src/Tangent.Graph` — `tangent.graph.json` for the viewer: layers, contracts, rules, the file table, nodes, edges, violations and exemptions (schema version 2).
- `src/Tangent.LanguageServer` — LSP server for `.tan` files.
- `viewer/` — the explorer (Vite, React, Tailwind v4): Issues, Rules, Coverage and a rooted Graph, with one shared detail panel. Pure logic lives in `src/graph/` (`parse`, `derive`, `explore`, `layout`) and is unit-tested against the real demo export.
- `editors/vscode`, `editors/rider` — editor integrations.
- `samples/Orders` — sample with deliberate violations. Drives the tests.
- `demo/Scheduling` — demo API that consumes the packed analyzer and CLI from `artifacts/packages`.

## Build and test

```sh
dotnet build Tangent.slnx
dotnet test Tangent.slnx
```

Viewer: `cd viewer && npm ci && npm test` (also `npm run typecheck`, `npm run dev`).

To see the demo in the viewer with code snippets and editor links:

```sh
cd demo/Scheduling
dotnet tangent graph Scheduling.slnx --include-unbound --include-source -o ../../viewer/public/scheduling.local.graph.json
```

then `cd ../../viewer && npm run dev` and open `?graph=/scheduling.local.graph.json`. Graphs exported with `--include-source` record an absolute path, so `public/*.local.graph.json` is git-ignored.

## The DSL, in one page

`.tan` files live in `.tangent/`. Two keywords carry every constraint: `require` (the default) and `forbid`.

| Rule | Example | Tier |
|---|---|---|
| Modifiers | `sealed`, `forbid public fields` | symbol |
| Base type / interfaces | `inherits Entity<*>`, `implements I{Name}` | symbol |
| Closed sets | `implements only I{Name}, IDisposable`, `inherits only RepositoryBase` | symbol |
| Attributes | `attributes [ApiController], [Route(*)]` | symbol |
| Signatures | `methods where public { returns Task<*> }` | symbol |
| Injection | `ctor { injects only IMediator, ILogger<*> }` | symbol |
| Accessors | `properties { forbid public set }` | symbol |
| Naming | `require name ends with "Handler"` | symbol |
| Location | `require lives in Application`, `interface I{Name} lives in Domain` | symbol |
| Namespace references | `forbid using Orders.Infrastructure.**` | reference |
| Contract references | `forbid using contract Repository` | reference |
| Object creation | `forbid new DbContext` | reference |
| Layers | the `project` block's `->` lines, plus `allow` for external packages | reference |
| Constructor flow | `require each parameter assigned to private readonly field`, `null-checked`, `not stored` | flow |
| State | `stateless` | flow |

Selectors: `match: <kind> in <layer or glob> [where <condition>]`, where a condition combines `name starts/ends with`, `name contains`, `name matches`, `inherits`, `implements`, `attributes` and modifiers with `and`, `or`, `not` and parentheses. Member blocks (`ctor`, `methods`, `properties`, `fields`, `events`, `nested`) and `where { }` refinements narrow which entities a rule applies to.

`contract B extends A` may only tighten A. `bind Type to Contract` and `[Tangent("Contract")]` bind explicitly; `exempt Type from Rule reason "..."` is the escape hatch, and every exemption is reported.

**The `allow` list is load-bearing.** A namespace that no layer declares is ignored *unless* the `project` block declares `allow`. With an allow list, every external dependency is declared or reported as TNG0205. `System.**` and `Tangent.**` are always allowed.

Diagnostics: TNG01xx structural, TNG02xx reference and layers, TNG03xx flow, TNG10xx model errors, TNG9000 exemption notes. `docs/diagnostics.md` is the catalogue, and `tests/Tangent.Core.Tests/cases/` holds a golden file per language feature.

## Conventions

- Central package management in `Directory.Packages.props`. The analyzer targets Roslyn 4.8.0; other projects use `VersionOverride` for 5.0.0.
- `samples/Orders` and `demo/Scheduling` have their own `Directory.Build.props` and do not import the root one.
- Re-packing keeps version 0.1.0. Before you rebuild the demo, clear `~/.nuget/packages/tangent.*` and run `dotnet build-server shutdown`.
- Do not edit `.tangent/` contracts to make a violation go away. Fix the code, or ask.

## Commit messages

- One short line. No body, no paragraphs.
- Plain English in a loose Simplified Technical English style: imperative mood, simple words, one idea.
- Examples: `Add the graph exporter`, `Fix the props folder probe`, `Update the plan`.
- No AI attribution. No `Co-Authored-By` trailers, no "Generated with" lines.
