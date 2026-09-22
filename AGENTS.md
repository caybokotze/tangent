# AGENTS.md

Context for coding agents working in this repository.

## Project

Tangent is an architectural conformance checker for .NET. Contracts are written in a small DSL (`.tan` files in a `.tangent/` folder) and checked by a Roslyn analyzer, a CLI, and a language server. `TANGENT-PLAN.md` is the design document and roadmap; read it before large changes.

## Layout

- `src/Tangent.Core` — lexer, parser, binder, rule IR. Roslyn-free.
- `src/Tangent.Analyzers` — Roslyn analyzer and code fixes. Also packs Core and Generators.
- `src/Tangent.Generators` — optional contract constants generator.
- `src/Tangent.Cli` — `tangent` tool: `init`, `lint`, `check`, `diff`, `graph`, `serve`.
- `src/Tangent.Graph` — graph export for the viewer.
- `src/Tangent.LanguageServer` — LSP server for `.tan` files.
- `viewer/` — React Flow graph viewer (Vite).
- `editors/vscode`, `editors/rider` — editor integrations.
- `samples/Orders` — sample with deliberate violations. Drives the tests.
- `demo/Scheduling` — demo API that consumes the packed analyzer and CLI from `artifacts/packages`.

## Build and test

```sh
dotnet build Tangent.slnx
dotnet test Tangent.slnx
```

Viewer: `cd viewer && npm ci && npm test`.

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
