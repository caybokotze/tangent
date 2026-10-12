# Tangent.Cli

The `tangent` command for [Tangent](https://github.com/caybokotze/tangent), an architectural conformance checker for .NET.

The CLI contains the same analyzer as [`Tangent.Analyzers`](https://www.nuget.org/packages/Tangent.Analyzers), so `tangent check` works on a solution that references no Tangent package at all.

## Install

As a local tool, so the team and CI use the same version:

```bash
dotnet new tool-manifest
dotnet tool install Tangent.Cli
dotnet tangent --help
```

Or globally:

```bash
dotnet tool install --global Tangent.Cli
```

## Commands

| Command | What it does |
|---|---|
| `tangent init` | Writes `.tangent/project.tan` with one layer per root namespace of the solution, and a starter `contracts.tan`. |
| `tangent lint` | Parses and binds the `.tan` files, and resolves their type names against the solution. |
| `tangent check` | Runs every contract over the solution. Writes SARIF with `--sarif` and JSON with `--json`. `--explain` adds the rule text and the offending line to each violation. |
| `tangent diff` | Prints the code fixes as a unified diff. `--apply` writes them. |
| `tangent graph` | Exports `tangent.graph.json` for the Tangent viewer. |
| `tangent serve` | Runs the language server for `.tan` files over stdio. |

## In CI

```bash
dotnet tool restore
dotnet tangent check --sarif tangent.sarif
```

| Exit code | Meaning |
|---|---|
| 0 | No violation at or above `--fail-on`. The default is `error`, so warnings alone exit 0. |
| 1 | Violations at or above `--fail-on`. Use `--fail-on warning` to fail on warnings too. |
| 2 | The `.tan` model has errors, so nothing was checked. |
| 3 | The solution, a project or the `.tangent` folder could not be loaded. |

The full reference is [docs/cli.md](https://github.com/caybokotze/tangent/blob/main/docs/cli.md).
