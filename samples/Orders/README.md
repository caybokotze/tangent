# samples/Orders

A small, realistic clean-architecture solution used as Tangent's primary fixture. It is the thing
`tangent check`, the analyzer tests and the benchmarks run against, and it is the first thing a new
user should read to understand what a `.tan` file actually does.

It **builds cleanly** — zero warnings, zero errors — and it is **architecturally wrong on purpose**.
Every deliberate break is catalogued in [VIOLATIONS.md](VIOLATIONS.md).

```
samples/Orders/
├── Orders.slnx
├── Directory.Build.props        net10.0, implicit usings, no central package management,
│                                deliberately does NOT import the repository-root props
├── .tangent/                    the architecture, as Tangent sees it
│   ├── project.tan              layers and the direction dependencies may flow
│   ├── controllers.tan          contract Controller
│   ├── repositories.tan         contract Repository
│   ├── entities.tan             contract Entity, contract AggregateRoot extends Entity
│   ├── services.tan             contract DomainService, ViewModel, QueryHandler
│   └── bindings.tan             bind / exempt statements
└── src/
    ├── Orders.Domain/           entities, value objects, domain services, repository interfaces
    ├── Orders.Application/      local IMediator abstraction, queries, commands, handlers, view models
    ├── Orders.Infrastructure/   EF Core DbContext, repositories, SystemClock, Mediator
    └── Orders.Api/              ApiControllerBase, controllers, composition root
```

## Layers

`project.tan` declares four layers by namespace glob and the only dependencies allowed between them:

| Layer | Namespace | May depend on |
|---|---|---|
| `Api` | `Orders.Api.**` | `Application`, `Domain` |
| `Application` | `Orders.Application.**` | `Domain` |
| `Infrastructure` | `Orders.Infrastructure.**` | `Application`, `Domain` |
| `Domain` | `Orders.Domain.**` | nothing |

Layers are bound to **namespaces, not projects**. Two files exploit that to produce layer
violations without a circular project reference, which is how the mistake actually shows up in real
repositories:

- `src/Orders.Domain/Misplaced/NotificationDispatcher.cs` compiles into `Orders.Domain.dll` but
  declares `namespace Orders.Application.Notifications`.
- `src/Orders.Application/Misplaced/CacheKeys.cs` compiles into `Orders.Application.dll` but
  declares `namespace Orders.Infrastructure.Caching`.

The one honest layer violation is `Orders.Api.DependencyInjection.ServiceRegistration`: the
composition root has to name Infrastructure types, and `Api -> Application, Domain` forbids it.
That tension is real, it is left unresolved here on purpose, and it is open question #1 in
`TANGENT-PLAN.md` §16.

## Packages

Only one, `Microsoft.EntityFrameworkCore` (10.0.10) in `Orders.Infrastructure`. It is here so that
the `forbid using Microsoft.EntityFrameworkCore.**` and `forbid new DbContext` rules have a real
type to bite on. Everything else — MVC, `ILogger<T>`, DI — comes from the ASP.NET Core shared
framework, and the mediator is ~20 lines of local code rather than a MediatR dependency.

Every project has a `ProjectReference` to `src/Tangent.Attributes`, so `[Tangent]` and
`[TangentExempt]` compile. `TANGENT_ATTRIBUTES` is defined nowhere, so both attributes are stripped
from the emitted assemblies — that is the intended behaviour, and the analyzer still sees them
because it runs on source.

The analyzer package is deliberately **not** referenced. The CLI applies the rules.

## Reading the fixture

Three kinds of type live here, and the comments say which is which:

- **CONFORMING** — `Order`, `OrderLine`, `OrdersController`, `OrderRepository`,
  `GetOrderQueryHandler`, `PricingService`, and the view models. These must produce no diagnostics.
- **VIOLATION** — flagged inline with the expected diagnostic code, e.g.
  `// VIOLATION (TNG0107): Entity forbids public setters on properties.`
- **EXEMPTED** — a genuine break that an exemption suppresses, so the expected result is TNG9000
  (hidden) rather than a warning. There are three mechanisms on show:
  `exempt <type> reason "..."`, `exempt <type> from <contract> reason "..."`, and
  `[TangentExempt("...", Contract = "Controller")]` on a single method. `HealthController`
  additionally demonstrates `bind ... except [ProducesResponseType]`.

## Notes for the analyzer implementation

Two cases in here are load-bearing design questions rather than decided behaviour:

1. **Compiler-generated members.** Positional records (`OrderViewModel`) have implicit
   `<X>k__BackingField` fields that are private but not readonly. `contract ViewModel` declares
   `fields { require private; require readonly }`, so the evaluator must skip
   `ISymbol.IsImplicitlyDeclared` members or every view model reports a false positive.
2. **Subtype matching in `new`.** `contract Controller` declares `forbid new DbContext`, and
   `LegacyOrdersController` constructs an `OrdersDbContext`, which derives from `DbContext`. The
   same method also constructs an `HttpClient`, which is an exact-name match, so the fixture
   distinguishes the two cases.
