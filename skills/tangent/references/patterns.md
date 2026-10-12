# Contract patterns

Working contracts for the common kinds of type in a layered .NET solution. They are taken from the Orders sample in the Tangent repository and the Scheduling demo in tangent-demos, so the syntax is known to bind. Adapt the names, namespaces and type lists to the solution in front of you; do not copy a rule the team has not actually decided on.

## Contents

1. Layers (clean architecture)
2. Layers: a composition root
3. Controller
4. Request handler
5. Repository
6. Entity and aggregate root
7. Domain service
8. View model
9. Explicit bindings and ignores
10. Choosing the `match`

## 1. Layers (clean architecture)

```tangent
// The shape of the solution: four layers and the direction dependencies may flow.
// Anything not named on a `->` line is forbidden.
project "Scheduling" {
    root: "src/"

    layer Api            = Scheduling.Api.**
    layer Application    = Scheduling.Application.**
    layer Domain         = Scheduling.Domain.**
    layer Infrastructure = Scheduling.Infrastructure.**

    Api            -> Application, Domain
    Application    -> Domain
    Infrastructure -> Application, Domain
    Domain         -> nothing

    // Every external package these layers may touch. Declaring an allow list switches the
    // model to strict externals: a namespace no layer declares and no glob below matches is
    // then TNG0205. System.** is always allowed.
    allow Microsoft.AspNetCore.**, Microsoft.Extensions.**, Microsoft.OpenApi.**
}
```

Notes:
- `Domain -> nothing` does **not** keep EF Core out of the Domain on its own: without an `allow` line, external namespaces are not checked. Either add an `allow` list, or add `forbid using Microsoft.EntityFrameworkCore.**` to the Domain's contracts.
- With an `allow` list, list the packages each layer legitimately uses, and run the check: every TNG0205 is either a package to add or a dependency to question. Ask the user which.
- Infrastructure pointing at Application is normal when Application declares the interfaces Infrastructure implements (repositories, clocks, message buses).

## 2. Layers: a composition root

The code that registers services (`AddXxx(this IServiceCollection ...)`) has to name Infrastructure types, so in the layout above every registration line is a TNG0202 from Api to Infrastructure. Give it a layer of its own. The most specific glob wins, so the nested namespace leaves `Api` and joins `Composition`:

```tangent
    layer Api            = Orders.Api.**
    layer Composition    = Orders.Api.DependencyInjection.**
    ...
    Api            -> Application, Domain, Composition
    Composition    -> Application, Domain, Infrastructure
```

Do not add `Api` to the `Composition` line: `Api -> Composition -> Api` is a cycle (TNG1006). This keeps the rest of `Api` honest while allowing exactly one folder to wire the application together.

## 3. Controller

```tangent
// Controllers are thin, sealed adapters over the mediator. They translate HTTP into a
// request and a view model back into a response, and they do nothing else.
contract Controller {
    match: class in Api where name ends with "Controller"

    inherits ApiControllerBase
    sealed
    attributes [ApiController], [Route(*)]

    ctor {
        require public
        injects only IMediator, ILogger<*>
        require each parameter assigned to private readonly field
    }

    methods where public {
        returns Task<ActionResult<*>> | Task<IActionResult>
        attributes [HttpGet | HttpPost | HttpPut | HttpDelete | HttpPatch]
        attributes [ProducesResponseType(*)]
    }

    forbid using Scheduling.Infrastructure.**
}
```

Adapt: the base class (`ControllerBase` if there is no project base), the injection list (a solution without MediatR injects application services instead), and whether `ProducesResponseType` is really required. Minimal APIs have no controllers; write a contract for the endpoint classes instead, or skip it.

## 4. Request handler

```tangent
// Request handlers are the only Application types that orchestrate the Domain.
// One handler per request, sealed, with every collaborator injected into a private readonly field.
contract Handler {
    match: class in Application where name ends with "Handler"

    implements IRequestHandler<*, *>
    require name ends with "Handler"
    require lives in Application
    sealed

    ctor {
        injects only IAppointmentRepository, IProviderRepository, ISlotFinderService, IClock, IUserContext
        require each parameter assigned to private readonly field
    }

    methods where public { returns Task<*> }

    forbid using Scheduling.Infrastructure.**
}
```

Adapt: `injects only` is the strongest rule here and the one that ages worst. List what handlers inject today, then ask the user whether that list is a decision or an accident. An alternative that ages better is `forbid using contract Repository` on the types that must not reach storage.

A match on an interface catches handlers whatever they are called: `match: class in Application where implements IRequestHandler<*, *> and not abstract`, combined with `require name ends with "Handler"` to enforce the naming.

## 5. Repository

```tangent
// Repositories are the only types that own storage, and the interface a repository
// implements belongs to the Domain that consumes it, never to Infrastructure.
contract Repository {
    match: class in Infrastructure where name ends with "Repository"

    implements only I{Name}
    interface I{Name} lives in Domain
    sealed

    // A repository owns one aggregate's storage, so it never reaches for another repository.
    forbid using contract Repository

    methods where public { returns Task<*> }

    forbid using Scheduling.Api.**
}
```

`I{Name}` expands per type: `OrderRepository` must implement `IOrderRepository`, declared in the Domain layer. Use `implements` instead of `implements only` when repositories legitimately implement other interfaces too.

## 6. Entity and aggregate root

```tangent
// Entities are created through factories and changed through behaviour, never through
// property setters, and they know nothing about persistence or serialisation.
contract Entity {
    match: class in Domain where inherits Entity<*> | AggregateRoot<*>

    properties {
        forbid public set            // init / private set only
    }
    forbid public fields
    ctor { forbid public }           // factories only

    forbid using System.Data.**, System.Text.Json.**, Microsoft.**
}

contract AggregateRoot extends Entity {
    match: class in Domain where inherits AggregateRoot<*>
    // May only add rules; it cannot loosen Entity.
    methods where public { forbid returns void }   // commands return a result
}
```

`extends` gives the stricter variant every Entity rule plus its own. Matching on the base class (`inherits Entity<*>`) rather than a namespace keeps value objects and enums in the same folder out of the contract.

## 7. Domain service

```tangent
// Domain services are sealed, stateless collaborators behind an interface.
contract DomainService {
    match: class in Scheduling.Domain.Services.**

    implements I{Name}
    sealed
    stateless
    fields { require private; require readonly; forbid static }

    forbid using Scheduling.Application.**, Scheduling.Infrastructure.**
}
```

The `forbid using` lines repeat what the layer matrix already forbids. That is deliberate when the contract should carry its own reason, but it doubles every violation; drop them if the layers already say it.

## 8. View model

```tangent
// View models are immutable transport records that carry no domain types across the wire.
contract ViewModel {
    match: record in Scheduling.Application.ViewModels.**

    properties {
        require public
        require get
        forbid public set
    }
    fields { require private; require readonly }

    forbid using Scheduling.Domain.**
}
```

## 9. Explicit bindings and ignores

```tangent
// A liveness probe has no response body worth documenting, so it keeps the Controller
// contract minus the one rule that does not apply to it.
bind Scheduling.Api.Controllers.HealthController to Controller except [ProducesResponseType]

// An append-only audit row is materialised field by field by the persistence layer.
ignore Scheduling.Domain.Appointments.AppointmentAuditEntry from Entity reason "append-only audit row, written field by field by the repository"
```

Prefer, in order: a `match` that fits, then `bind ... except` for one rule on one type, then `ignore` for a type that is deliberately different. Keep all of these in one `bindings.tan` with a comment each, so the exceptions are reviewed together.

## 10. Choosing the `match`

| Situation | Match |
|---|---|
| Naming convention is reliable | `class in Api where name ends with "Controller"` |
| A base class or interface defines the kind | `class in Domain where inherits AggregateRoot<*>` |
| A folder/namespace defines the kind | `class in Orders.Domain.Services.**` |
| Several of these, and abstract bases must be excluded | `class in Application where implements IRequestHandler<*, *> and not abstract` |
| Records only | `record in Orders.Application.ViewModels.**` |

After writing a match, check what it binds: `tangent graph --include-unbound -o graph.json`, then look at `nodes[].contracts`. A match that also binds helpers, base classes or test doubles produces violations that look like debt but are really a wrong match.
