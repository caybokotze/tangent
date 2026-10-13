# Tangent

> A line is tangent to a curve when it touches it at exactly one point. Tangent checks that your codebase touches its architectural contracts exactly where it should, and nowhere else.

A deterministic tool to align non-deterministic code generation. The main point being that you can create more consistent output from an architectural perspective with AI agents by enforcing deterministic rules to re-enforce patterns and prevent architectural drift.

## WTF is Tangent?

**In one line Tangent is SonarCloud for software architecture. It is a deterministic architectural validator.**

## Use it

Tangent is in preview, so the install commands need `--prerelease`.

```bash
dotnet add package Tangent.Analyzers --prerelease   # in each project, or once in Directory.Build.props
dotnet new tool-manifest                             # once per repository
dotnet tool install Tangent.Cli --prerelease
dotnet tangent init                                  # writes .tangent/ from your namespaces
```

Add a contract to `.tangent/handlers.tan`:

```tangent
contract Handler {
    match: class in Shop.Application.** where name ends with "Handler"

    sealed
    ctor { injects only IOrderRepository }
}
```

This handler breaks it twice:

```csharp
using Shop.Infrastructure;

namespace Shop.Application;

public class PlaceOrderHandler(IOrderRepository orders, ShopDbContext db)
{
    // ...
}
```

`dotnet build` reports both, with the rule and the `.tan` line that declares it:

```
Application\PlaceOrderHandler.cs(5,14): warning TNG0101: Handler: type must be sealed. (rule Handler/type/modifier#0, handlers.tan:4)
Application\PlaceOrderHandler.cs(5,71): warning TNG0106: Handler: constructor may only inject IOrderRepository; found Shop.Infrastructure.ShopDbContext db. (rule Handler/ctor/injects-only#0, handlers.tan:5)
```

Seal the class and go through the repository, and the warnings go away:

```csharp
public sealed class PlaceOrderHandler(IOrderRepository orders)
```

`dotnet tangent diff --apply` adds the missing `sealed` for you. The dependency is a design decision, so you fix that one by hand. The same rules run in the IDE, in `dotnet tangent check` for CI, and in the [viewer](https://caybokotze.github.io/tangent/) through `dotnet tangent graph`. [Getting started](docs/getting-started.md) has the full walk-through.

## Why?

Agentic and AI development is here to stay and if used correctly can really boost productivity. However there are notable risks involved. When I use AI for programming it might generally be for incremental changes with a harness like Github Copilot, Claude Code, Opencode Codex or whatever else is hot right now. What I have found happening over and over is that I might use a frontier model for the planning or core development and cheaper models for bulk execution. When introducing multiple agents or loop-engineering with agents what tends to happen is you experience some drift from your target architecture. AI is non-deterministic. As a big believer in quality and good architecture I knew there was a solution for this. Tangent is what that solution has become. A way of building out deterministic contracts, so that when your AI drifts off and breaks that contract there is something that will notify you. You can use this information to then guide the agent back on track.

Static analysers look at code compositional patterns: this method is too complex, this variable is never read, this string comparison is culture-sensitive. They are excellent at it, and they say nothing at all about whether your controllers reach into the database, whether your domain layer has quietly taken a dependency on EF Core, or whether the repository someone added last sprint looks anything like the other nine. This might be possible to some extent but can become very tedious.

Tangent checks structure in the large, against **your** rules rather than a vendor's generic ruleset. You describe the shape of your codebase in a small declarative language (`.tan` files in a `.tangent/` folder at the solution root), and Tangent reports every place the code deviates: as IDE squiggles, as `dotnet build` warnings, as a CI failure, and as a diff of the changes that would bring the code into line.

The rules live next to the code, in version control, reviewed like code. When the architecture changes, the change is a diff someone approved — not a decision that eroded over eighteen months of pull requests nobody had time to read properly.

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

Rules are evaluated against Roslyn's semantic model, never against source text, so they survive reformatting, `partial` classes, primary constructors and file-scoped namespaces.

### Closed sets, naming, location, and references by contract

```tangent
contract Repository {
    match: class in Infrastructure where name ends with "Repository" and not abstract

    implements only I{Name}              // its own interface, and nothing else
    inherits only RepositoryBase         // one allowed base type, or none
    forbid using contract Repository     // no repository reaches for another repository
}

contract Handler {
    // select by what a type *is*, then require what it should be called and where it belongs
    match: any in Sample.** where implements IRequestHandler<*> and not abstract

    require name ends with "Handler"
    require lives in Application
    methods where public { require name ends with "Async" }
}
```

- **`implements only` / `inherits only`** are the closed-set forms of `implements` and `inherits`, as `injects only` is for `injects`. Interfaces that an allowed interface itself extends stay allowed, so `implements only I{Name}` does not fight inheritance.
- **`require name ...` and `require lives in ...`** turn what `match` uses as a selector into a rule. That is how you say "a handler is anything implementing `IRequestHandler<*>`, and it must be named `*Handler` and live in Application".
- **`forbid using contract X`** follows the contract, not the namespace. It catches a reference in a method body that `injects only` never sees, and it keeps working when the type moves.
- **Conditions take `not` and parentheses**: `where (implements IHandler or implements IJob) and not abstract`.

## Components

| Component | Delivered as | What it does |
|---|---|---|
| Analyzer | NuGet `Tangent.Analyzers` | Roslyn analyzer + code fixes. Squiggles in Rider/VS/VS Code, `dotnet build` diagnostics. |
| Attributes | NuGet `Tangent.Attributes` | `[Tangent("Controller")]`, `[TangentIgnore("reason")]`. Stripped from output assemblies. |
| CLI | .NET tool `dotnet tangent` | `init`, `lint`, `check`, `diff`, `graph`, `serve`. SARIF and JSON output. |
| Language server | `tangent serve` + `editors/vscode` | Diagnostics, completion, hover, go-to-definition for `.tan` files. |
| Viewer | [caybokotze.github.io/tangent](https://caybokotze.github.io/tangent/), built from `viewer/` | Explores `tangent.graph.json`: issues, rules, coverage and dependencies. Reads the file in your browser. |

Everything sits on one **rule IR** in `Tangent.Core`. The DSL compiles to the IR, the analyzer evaluates the IR, the graph exporter reads the IR. No component bypasses it.

---

## Architectural templates

A `.tan` file is a **template for a shape of code**. The same language spans the whole range, from a one-line statement of the dependency rule down to the precise anatomy of a single design pattern. Templates are portable: the contract that describes a repository in one solution describes it in the next one, with the namespace globs swapped.

Every snippet below is real, parseable `.tan` — they are checked against the parser, not written from memory.

### Architecture styles

The `project` block is where the big rules live. Layers are namespace globs; the `->` lines are the complete list of what may depend on what. **Anything not listed is forbidden**, which is what makes the dependency rule enforceable rather than aspirational.

```tangent
project "Orders" {
    root: "src/"

    layer Api            = Orders.Api.**
    layer Application    = Orders.Application.**
    layer Domain         = Orders.Domain.**
    layer Infrastructure = Orders.Infrastructure.**

    allow Microsoft.Extensions.**      // the external packages these layers may use

    Api            -> Application, Domain
    Application    -> Domain
    Infrastructure -> Application, Domain
    Domain         -> nothing          // the dependency rule, in one line
}
```

That single block expresses Clean Architecture's dependency rule, Onion's inward-only references, and the "domain knows nothing" invariant at the heart of both. **`allow` closes the external hole**: without it, a namespace that no layer declares (an external package) is ignored, so `Domain -> nothing` would not stop the domain from taking a dependency on EF Core. Declare one and every external dependency is either listed or a violation (TNG0205). `System.**` and Tangent's own attributes are always allowed. Variations are a matter of how you draw the lanes:

- **Hexagonal / ports and adapters** — ports are interfaces in `Domain`, adapters are classes in `Infrastructure` that implement them. The `Adapter` contract below enforces that an adapter's port actually lives on the domain side.
- **Vertical slice** — one layer per feature rather than per technical concern (`layer Orders = Shop.Features.Orders.**`), with `-> nothing` between slices so features cannot reach sideways into each other.
- **Modular monolith** — one layer per module, plus a `forbid using <Module>.Internal.**` contract so modules talk only through their published surface.
- **Anti-corruption layer** — a contract on the boundary types that forbids them from touching the domain model directly.

```tangent
contract LegacyGateway {
    match: class in Orders.Infrastructure.Legacy.**
    sealed
    forbid using Orders.Domain.Entities.**
}
```

### Domain-driven design

DDD's tactical patterns are mostly statements about the shape of a type, which is exactly what a contract is.

```tangent
contract Entity {
    match: class in Orders.Domain.Entities.**
    inherits Entity<*> | AggregateRoot<*>
    properties { forbid public set }      // mutate through behaviour, not setters
    forbid public fields
    ctor { forbid public }                // construct through factories
}

contract AggregateRoot extends Entity {
    match: class in Orders.Domain.Entities.** where inherits AggregateRoot<*>
    methods where public { forbid returns void }   // commands return a result
}

contract ValueObject {
    match: record in Orders.Domain.ValueObjects.**
    sealed
    stateless
    properties { require public; require get; forbid set }
    forbid public fields
}

contract DomainEvent {
    match: record in Orders.Domain.Events.**
    sealed
    implements IDomainEvent
    properties { require get; forbid set }
}

contract Repository {
    match: class in Infrastructure where name ends with "Repository"
    implements I{Name}
    interface I{Name} lives in Domain     // the port belongs to the domain
    sealed
    ctor { injects only OrdersDbContext }
    methods where public { returns Task<*> }
    forbid using Orders.Api.**
}

contract Specification {
    match: class in Orders.Domain.Specifications.**
    sealed
    implements ISpecification<*>
    stateless
    fields { require private; require readonly }
}
```

`extends` is how a family of contracts stays coherent: `AggregateRoot` inherits every rule `Entity` declares and may only add to them. A child that loosens a parent is a compile-time error (TNG1011), so the hierarchy can never be weakened by accident.

Note `I{Name}` — a **template**. It expands per bound type, so `OrderRepository` must implement `IOrderRepository` and `CustomerRepository` must implement `ICustomerRepository`, from one rule. `{Namespace}` and `{Name:strip Suffix}` are also available.

### Creational patterns

```tangent
contract Factory {
    match: class in Orders.Domain.Factories.** where name ends with "Factory"
    sealed
    stateless
    implements I{Name}
    methods where public { forbid returns void }
}

contract Builder {
    match: class in Orders.Application.Builders.** where name ends with "Builder"
    sealed
    forbid public fields
    methods where public { forbid returns void }   // fluent: every step returns something
}

contract Options {
    match: class in Orders.Api.Configuration.** where name ends with "Options"
    sealed
    properties { require public; require get; require set }
    forbid public fields
}
```

The other half of the factory pattern is enforced on the *product*, not the factory: `ctor { forbid public }` on `Entity` above is what actually forces construction to go through the factory. Patterns are usually a pair of contracts like this — one on the thing, one on the thing that makes it.

Also expressible: **object pool** (`stateless` plus `fields { require private; require readonly }` on the pool's clients), **prototype** (`implements ICloneable<*>`), and **dependency injection discipline** generally — `injects only A, B` is the single most load-bearing rule in most codebases, because it pins down exactly what a type is allowed to depend on.

### Structural patterns

```tangent
contract Adapter {
    match: class in Orders.Infrastructure.Adapters.** where name ends with "Adapter"
    sealed
    implements I{Name:strip Adapter}               // PaymentGatewayAdapter -> IPaymentGateway
    interface I{Name:strip Adapter} lives in Domain
    forbid using Orders.Api.**
}

contract Decorator {
    match: class in Orders.Application.Decorators.** where name ends with "Decorator"
    sealed
    implements I{Name:strip Decorator}             // CachingOrderReaderDecorator -> ICachingOrderReader
    ctor { injects only I{Name:strip Decorator} }  // wraps exactly one of what it implements
    fields { require private; require readonly }
}
```

The decorator contract is a good illustration of what templates buy you: "implements the interface it wraps, and wraps exactly one of them" is a single rule that holds for every decorator you will ever write, checked against the real symbol graph.

Also expressible: **facade** (a sealed type whose public surface returns only DTOs and which forbids leaking domain types), **proxy** (same shape as decorator), **composite** (`implements I{Name}` plus `injects only IEnumerable<I{Name}>`), **bridge** (abstraction layer forbidden from referencing the implementation namespace), **flyweight** (`stateless` plus `require static`).

### Behavioural patterns

```tangent
contract Strategy {
    match: class in Orders.Domain.Pricing.** where name ends with "Strategy"
    sealed
    implements IPricingStrategy
    stateless                                  // no mutable instance state
    fields { require private; require readonly }
}

contract Command {
    match: record in Orders.Application.Commands.**
    sealed
    implements IRequest<*>
    properties { require public; require get; forbid public set }
    forbid public fields
}

contract CommandHandler {
    match: class in Application where name ends with "Handler"
    sealed
    implements IRequestHandler<*, *>
    ctor {
        injects only IOrderRepository, IUnitOfWork, IClock
        require each parameter assigned to private readonly field
    }
    methods where public { returns Task<*> }
    forbid using Orders.Infrastructure.**
}

contract PipelineBehavior {
    match: class in Orders.Application.Behaviors.** where name ends with "Behavior"
    sealed
    implements IPipelineBehavior<*, *>
    stateless
}

contract NotificationHandler {
    match: class in Orders.Application.Notifications.** where name ends with "EventHandler"
    sealed
    implements INotificationHandler<*>
    methods where public { returns Task }
}
```

`stateless` and `require each parameter assigned to private readonly field` are Tier 2 rules: they read the operation tree, not just the symbol. That is what lets Tangent say "this strategy has mutable state" or "this constructor takes a dependency and throws it away", which no signature-level check can see.

Also expressible: **chain of responsibility** (pipeline behaviours above), **template method** (`forbid public methods` on the concrete subclass, forcing the base to own the public entry point), **observer**, **state**, **visitor** (`implements IVisitor<*>` plus `forbid public fields`), **mediator** (the `Controller` contract at the top of this README is one — `injects only IMediator` is the whole pattern).

```tangent
contract ImportJob {
    match: class in Orders.Application.Imports.** where inherits ImportJobBase
    sealed
    forbid public methods      // the base class owns the algorithm; this only fills in steps
}
```

### Binding and escape hatches

Convention (`match:`) covers most types. Two escape hatches handle the rest, and both are explicit, greppable and reported:

```tangent
bind Orders.Api.Controllers.HealthController to Controller except [ProducesResponseType]
ignore Orders.Infrastructure.Legacy.OldPricingGateway from LegacyGateway reason "removed in Q3"
```

A `reason` is required by the grammar, not by convention. Ignores show up in `tangent check --show-ignored`, so the debt is countable.

### What the DSL deliberately cannot do

There are no expressions, no conditionals, no loops, and no general query language. Every construct maps to exactly one IR node. This is a constraint, not an oversight: it keeps rules fast enough to run on every keystroke inside the compiler, and it keeps them readable by someone who has never seen the language before. If a rule needs real computation, it needs a hand-written Roslyn analyzer, not a bigger DSL.

Tangent also constrains **shape, not quality**. It can tell you a type is in the wrong layer, has the wrong base class, or takes a dependency it shouldn't. It cannot tell you the domain model is incoherent.

---

## Tangent for agentic development

The bottleneck in agent-assisted coding is not producing plausible code — it is holding global invariants steady across hundreds of local edits. An agent will happily write a perfectly reasonable controller that reaches straight into the `DbContext`, because nothing in its context said not to and nothing in its feedback loop caught it. Multiply that by a week of autonomous work and the architecture is gone.

A machine-checkable architecture spec is the missing signal. Write the contracts first, let agents do the work, then verify conformance deterministically.

Four properties make Tangent a good fit, none of which were designed with agents in mind:

- **The DSL reads as a specification.** A `.tan` file is a precise, compact statement of intent that is also a good prompt. Put `.tangent/` in the agent's context and it writes conformant code the first time, instead of being corrected afterwards.
- **Rule ids are stable by construction.** `Controller/ctor/injects-only#0` is derived from structure, never from source position, so it survives reformatting, moving a rule between lines, and adding comments. "You broke this rule" stays meaningful across iterations, which is what makes a fix-loop converge instead of drift.
- **Output is already machine-readable.** SARIF from `tangent check`, plus ordinary compiler diagnostics from the analyzer.
- **`tangent diff` compares two models**, which is how you detect the architecture being weakened rather than the code being fixed.

### Wiring it in

Tangent is a CLI and a Roslyn analyzer, so it is agnostic about which harness you run — Claude Code, Codex, OpenHands, Aider, or a loop you wrote yourself. Layer it, so each stage catches what the previous one missed:

1. **Context.** Contracts in the system prompt or `CLAUDE.md`. Prevention beats correction.
2. **Hook.** Run `tangent check --explain` on file write or pre-commit. Catching it immediately beats catching it at the end, and it does not depend on the agent remembering to look.
3. **Tool.** Expose `tangent check --explain --json out.json` (or `--sarif out.sarif`) as a callable tool so the agent can self-correct in a loop.
4. **Gate.** CI blocks the PR. The backstop that does not negotiate.

The highest-leverage step needs no integration at all: `Tangent.Analyzers` runs **inside the compiler**, so violations land in ordinary `dotnet build` output. Any agent that reads build output gets architectural feedback for free.

Use `--explain` wherever an agent reads the output. A rule id is a lookup key for a human; the rule text is directly actionable for a model. With `--explain`, each violation carries the rule rendered back to DSL, the `.tan` line that declares it, the `match` that applies it, and the offending line of code:

```
  warning TNG0203 src/Orders.Api/Controllers/LegacyOrdersController.cs:28:26  Controller: forbidden object creation: new HttpClient. (rule Controller/methods/new#1, controllers.tan:20)
      rule        methods where public { forbid new HttpClient }
      declared    .tangent/controllers.tan:20
      applies to  contract Controller matches class in Api where name ends with "Controller"
      28 | using var http = new HttpClient();
         |                  ^^^^^^^^^^^^^^^^
```

### The failure mode to design for

**Do not give the agent write access to `.tangent/`.**

An agent that cannot satisfy a rule will eventually relax the rule, or add an ignore with a plausible-sounding reason, and report success. That is Goodhart's law in its purest form, and it collapses the entire value proposition: a verifier the subject can edit verifies nothing.

- Make `.tangent/` human-owned and read-only at the permission layer.
- Gate on `tangent diff` showing no loosening against a baseline. The concept already exists as TNG1011 for parent/child contracts; the same comparison applied to model-versus-baseline catches a weakened `project` block or a deleted rule.
- Track ignore count as a first-class metric. The grammar forces a `reason`; nothing forces it to be *true*. New ignores should need a human.

### Roadmap ideas

- **A template library** — `clean-architecture.tan`, `ddd-tactical.tan`, `cqrs.tan` as starting points to copy and adapt, so a new project gets a defensible architecture on day one rather than inheriting one by accident.
- **Tier-scoped checks** — run Tier 0 in the tight edit loop and the full set at PR time, since the tiers already encode exactly this cost gradient.

### What this does not give you

Conformance is not correctness. An agent can produce code that is fully conformant and architecturally thoughtless: correct layering, sealed types, injected dependencies, and a domain model that makes no sense. Tangent is a guardrail against drift and a fast, objective signal in a loop that otherwise has none. It is not a substitute for review.

---

## Build from source

```bash
# Build everything
dotnet build Tangent.slnx

# Run the tests (the CLI tests load samples/Orders, so restore it first)
dotnet restore samples/Orders/Orders.slnx
dotnet test Tangent.slnx

# Pack the analyzer, attributes and CLI into artifacts/packages (version 0.1.0-dev)
dotnet pack Tangent.slnx -c Release

# Pack a release: the version comes from the git tag
dotnet pack Tangent.slnx -c Release -p:Version=0.1.0-preview.1

# Check the sample solution
dotnet run --project src/Tangent.Cli -- check samples/Orders/Orders.slnx
```

## Repository layout

```
src/         Tangent.Core, Tangent.Attributes, Tangent.Analyzers, Tangent.Generators,
             Tangent.Graph, Tangent.Cli, Tangent.LanguageServer
tests/       Core golden tests, analyzer verifier tests, CLI end-to-end tests, benchmarks
samples/     Orders: a clean-architecture sample with deliberate violations
editors/     VS Code extension (TextMate grammar + LSP client)
viewer/      React graph viewer
docs/        Design notes and API docs
```

## Documentation

- [Getting started](docs/getting-started.md): install, describe, bind, enforce.
- [CLI reference](docs/cli.md): every command, option, exit code and output format.
- [Diagnostics](docs/diagnostics.md): every `TNG` code with its meaning and default severity.
- [Core API](docs/core-api.md), [Analyzer API](docs/analyzer-api.md), [Language server](docs/language-server.md), [Benchmarks](docs/benchmarks.md).
- [tangent-demos](https://github.com/caybokotze/tangent-demos): runnable solutions that use the packages, starting with a Scheduling API.

## Status

| Phase | Scope | State |
|---|---|---|
| 0 | Skeleton, packaging, attributes, `samples/Orders` | Done |
| 1 | Lexer, parser, binder, IR, `tangent lint`, TextMate grammar | Done |
| 2 | Analyzer Tier 0, `tangent check`, SARIF, benchmarks | Done |
| 3 | Tier 1 (`forbid using`, `forbid new`, layers), code fixes, `tangent diff`, `extends`, refinements | Done |
| 4 | Tier 2 (parameter flow, `stateless`), templates, source generator | Done |
| 5 | `tangent graph` and the React viewer | Done |
| 6 | Language server and VS Code extension | Done (Rider plugin is an unverified scaffold in `editors/rider`) |
| 7 | Viewer v2: Issues, Rules, Coverage and rooted Graph views; file table and `--include-source` | Done |
