# Tangent

> A line is tangent to a curve when it touches it at exactly one point. Tangent checks that your codebase touches its architectural contracts exactly where it should, and nowhere else.

Tangent is an **architectural conformance checker for .NET**. You describe the shape of your codebase in a small declarative language (`.tan` files in a `.tangent/` folder at the solution root), and Tangent reports every place the code deviates: as IDE squiggles, as `dotnet build` warnings, as a CI failure, and as a diff of the changes that would bring the code into line.

```tangent
project "Orders" {
    layer Api            = Orders.Api.**
    layer Application    = Orders.Application.**
    layer Domain         = Orders.Domain.**
    layer Infrastructure = Orders.Infrastructure.**

    Api            -> Application, Domain
    Application    -> Domain
    Infrastructure -> Application, Domain
    Domain         -> nothing
}

contract Controller {
    match: class in Api where name ends with "Controller"

    inherits ApiControllerBase
    sealed
    attributes [ApiController], [Route(*)]

    ctor {
        injects only IMediator, ILogger<*>
        require each parameter assigned to private readonly field
    }

    methods where public {
        returns Task<ActionResult<*>> | Task<IActionResult>
        attributes [HttpGet | HttpPost | HttpPut | HttpDelete | HttpPatch]
    }

    forbid using Orders.Infrastructure.**
}
```

## Components

| Component | Delivered as | What it does |
|---|---|---|
| Analyzer | NuGet `Tangent.Analyzers` | Roslyn analyzer + code fixes. Squiggles in Rider/VS/VS Code, `dotnet build` diagnostics. |
| Attributes | NuGet `Tangent.Attributes` | `[Tangent("Controller")]`, `[TangentExempt("reason")]`. Stripped from output assemblies. |
| CLI | .NET tool `dotnet tangent` | `init`, `lint`, `check`, `diff`, `graph`, `serve`. SARIF and JSON output. |
| Language server | `tangent serve` + `editors/vscode` | Diagnostics, completion, hover, go-to-definition for `.tan` files. |
| Viewer | `viewer/` (static React app) | Renders `tangent.graph.json`: layers, types, dependencies, violations. |

Everything sits on one **rule IR** in `Tangent.Core`. The DSL compiles to the IR, the analyzer evaluates the IR, the graph exporter reads the IR. No component bypasses it.

## Quick start

```bash
# Build everything
dotnet build Tangent.slnx

# Run the tests
dotnet test Tangent.slnx

# Pack the analyzer, attributes and CLI into artifacts/packages
dotnet pack Tangent.slnx -c Release

# Check the sample solution
dotnet run --project src/Tangent.Cli -- check samples/Orders/Orders.slnx
```

In your own solution:

```bash
dotnet add package Tangent.Attributes
dotnet add package Tangent.Analyzers
dotnet tool install -g Tangent.Cli
tangent init          # scaffolds .tangent/ from your namespaces
tangent check         # or just: dotnet build
```

## Repository layout

```
src/         Tangent.Core, Tangent.Attributes, Tangent.Analyzers, Tangent.Generators,
             Tangent.Graph, Tangent.Cli, Tangent.LanguageServer
tests/       Core golden tests, analyzer verifier tests, CLI end-to-end tests, benchmarks
samples/     Orders: a clean-architecture sample with deliberate violations
demo/        Scheduling API: an end-to-end consumer of the packaged analyzer and CLI
editors/     VS Code extension (TextMate grammar + LSP client)
viewer/      React graph viewer
docs/        Design notes and API docs
```

The full design is in [TANGENT-PLAN.md](TANGENT-PLAN.md).
