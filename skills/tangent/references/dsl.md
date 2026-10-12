# The Tangent contract language

Contracts are written in `.tan` files in the `.tangent/` folder. Every `.tan` file under that folder, recursively, is part of one model. File names carry no meaning; they are for people.

## Contents

1. Syntax basics
2. The project block: layers and dependencies
3. Contracts
4. Selectors: `match`, member blocks and `where`
5. Rule reference
6. Binding a type to a contract
7. Ignores
8. Rule ids
9. A complete example

## 1. Syntax basics

- `// line` and `/* block */` comments.
- Whitespace and newlines are not significant. `;` separates rules on one line: `fields { require private; require readonly }`.
- Two keywords carry every constraint: `require` and `forbid`. A rule with neither is a `require`.
- **Type patterns** use C# syntax with `*` as a wildcard: `Task<ActionResult<*>>`, `IRepository<*>`, `ILogger<*>`. `A | B` means either.
- **Namespace globs**: `*` matches one segment, `**` matches zero or more. `Orders.Api.**` is `Orders.Api` and everything below it.
- **Attributes**: `[Name]`, or `[Name(*)]` for any arguments.
- **Templates**: `{Name}` is the bound type's name, `{Namespace}` its namespace, `{Name:strip Suffix}` the name without a suffix. `implements I{Name}` on `OrderRepository` means `implements IOrderRepository`.

## 2. The project block: layers and dependencies

Usually in `project.tan`. There is one per model.

```tangent
project "Orders" {
    root: "src/"

    layer Api            = Orders.Api.**
    layer Application    = Orders.Application.**
    layer Domain         = Orders.Domain.**
    layer Infrastructure = Orders.Infrastructure.**

    allow Microsoft.Extensions.**

    Api            -> Application, Domain
    Application    -> Domain
    Infrastructure -> Application, Domain
    Domain         -> nothing
}
```

- A **layer** is a name for a namespace glob. A type belongs to the layer with the most specific matching glob.
- An **`->` line** lists the layers that a layer may reference. A layer may always reference itself. Anything not listed is forbidden and reported as **TNG0202**. `-> nothing` allows no other layer.
- `System.**` and `Tangent.**` are always allowed.
- **`allow`** matters more than it looks. Without any `allow` line, a namespace that belongs to no layer (an external package such as EF Core) is not checked at all. With at least one `allow` line, every external namespace must be allowed or it is reported as **TNG0205**.

## 3. Contracts

```tangent
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
        forbid new DbContext, new HttpClient
    }

    forbid using Orders.Infrastructure.**
}
```

A contract has a name, an optional `match:` clause, and rules. Rules at the top level apply to the type itself; rules inside a member block apply to each member of that kind.

**`extends`**: `contract AggregateRoot extends Entity { ... }` inherits every rule of `Entity` and may only add rules. A child rule that loosens a parent rule is a model error (TNG1011).

## 4. Selectors: `match`, member blocks and `where`

**`match: <kind> in <layer or glob> [where <condition>]`** decides which types the contract binds by convention.

- Kind: `class`, `interface`, `record`, `struct`, `enum` or `any`.
- `in Api` uses a layer; `in Orders.Domain.Entities.**` uses a namespace glob.
- Conditions: `name starts with "X"`, `name ends with "X"`, `name contains "X"`, `name matches "regex"`, `inherits T`, `implements T`, `attributes [A]`, and modifiers (`abstract`, `sealed`, `static`, `public`, ...). Combine with `and`, `or`, `not` and parentheses.

```tangent
match: class in Application where implements IRequestHandler<*, *> and not abstract
```

**Member blocks** scope rules to members: `ctor`, `methods`, `properties`, `fields`, `events`, `nested`. A block can have its own condition:

```tangent
methods where public { returns Task<*> }
properties { forbid public set }
```

**Refinements** narrow the selector further with `where <condition> { rules }`, at the top level or inside a member block:

```tangent
where name ends with "Admin" { forbid public fields }
properties where public { where readonly { forbid set } }
```

The more specific selector wins. Two contradicting rules at the same specificity are a model error (TNG1010).

## 5. Rule reference

| Rule | Example | Meaning | Code |
|---|---|---|---|
| Modifier | `sealed`, `require public`, `forbid static` | The type or member has (or lacks) the modifier | TNG0101 |
| Base type | `inherits Entity<*> \| AggregateRoot<*>` | Some base type in the chain matches | TNG0102 |
| Interface | `implements I{Name}` | Some implemented interface matches | TNG0103 |
| Closed interface set | `implements only I{Name}, IDisposable` | Every directly implemented interface is in the list | TNG0110 |
| Closed base set | `inherits only RepositoryBase` | The base type (ignoring `object`) is in the list | TNG0111 |
| Attributes | `attributes [ApiController], [Route(*)]` | `[A], [B]` needs both; `[A \| B]` needs either | TNG0104 |
| Return type | `methods where public { returns Task<*> }` | The member's return type matches. `forbid returns void` inverts it | TNG0105 |
| Injection | `ctor { injects only IMediator, ILogger<*> }` | Every constructor parameter type is in the list. `injects X` without `only` requires X to be one of them | TNG0106 |
| Accessors | `properties { forbid public set }`, `require init` | The property accessors | TNG0107 |
| Member kinds | `forbid public fields`, `forbid nested` | The type declares no member of that kind | TNG0108 |
| Interface location | `interface I{Name} lives in Domain` | The matching interface is declared in the layer | TNG0109 |
| Naming | `require name ends with "Handler"`, `methods { require name ends with "Async" }` | The type or member name passes the test | TNG0112 |
| Location | `require lives in Application`, `require lives in Orders.Application.**` | The type is in the layer or namespace | TNG0113 |
| Namespace references | `forbid using Orders.Infrastructure.**` | No reference anywhere in the type (signatures, attributes, bodies) resolves into the namespace | TNG0201 |
| Object creation | `forbid new DbContext, new HttpClient` | No `new` of the type or a type derived from it | TNG0203 |
| Contract references | `forbid using contract Repository` | No reference to any type bound to that contract, wherever it lives | TNG0204 |
| Parameter assignment | `require each parameter assigned to private readonly field` | Every constructor parameter is stored in such a field | TNG0301 |
| Null checks | `require each parameter null-checked` | Every constructor parameter is checked: `ThrowIfNull(p)`, `p ?? throw`, `if (p is null) throw` | TNG0302 |
| Not stored | `require each parameter not stored` | No constructor parameter is kept in a field or property | TNG0301 (message: "must not be stored") |
| Stateless | `stateless` | Instance fields are readonly and instance properties have no setter | TNG0303 |

Tiers: symbol rules (TNG01xx) look only at declarations and are the cheapest; reference rules (TNG02xx) look at every symbol a type uses; flow rules (TNG03xx) look at constructor bodies.

## 6. Binding a type to a contract

Three sources, and they add up: a type follows every contract it is bound to from any of them.

1. **`match:`** in the contract. Convention; binds most types, including new ones.
2. **`bind`** in a `.tan` file: `bind Orders.Api.Controllers.HealthController to Controller`. `except [ProducesResponseType]` or `except <rule id>` removes those rules for that type.
3. **`[Tangent("Controller")]`** on the type, from the `Tangent.Attributes` package. With the `Tangent.Analyzers` package, `[Tangent(Contracts.Controller)]` uses a generated constant, so a misspelt name is a compile error. Without it, a misspelt name is TNG0001.

A contract with no `match` and no `bind` is reported as TNG1030. A `match` that selects no types is not reported at all, so after writing one, check what it binds: `tangent graph --include-unbound -o graph.json`, then `nodes[].contracts`.

Type names in rules (`inherits`, `implements`, `injects`, `returns`, `new`) must resolve in the projects the contract covers, or `tangent lint`/`check` reports TNG1020. `forbid new DbContext` over a layer whose projects do not reference EF Core cannot resolve `DbContext`; write `forbid using Microsoft.EntityFrameworkCore.**` or `forbid using contract DbContexts` (a contract matching `class in Orders.** where inherits DbContext`) instead.

## 7. Ignores

An ignore removes a type, a member, or one contract or rule, from checking. Every ignore needs a reason, and every one is reported as TNG9000 (`tangent check --show-ignored`).

```tangent
ignore Orders.Infrastructure.LegacyReportRepository reason "migrating in Q4 2026"
ignore Orders.Domain.Entities.AuditEntry from Entity reason "append-only log, public setters by design"
```

```csharp
[TangentIgnore("calendar feed returns a file, not a view model", Contract = "Controller")]
public IActionResult GetCalendarFeed() { ... }

[TangentIgnore("legacy", Rule = "Controller/methods/attributes#1")]
```

On a type, the attribute covers the whole type; on a member, only that member. `Contract = ...` or `Rule = ...` narrows it.

Ignores are decisions for the people who own the architecture. Do not add one to make a check pass (see SKILL.md).

## 8. Rule ids

Every rule gets an id: `Contract/selector/predicate#n`, for example `Controller/methods/attributes#1`. The selector is the member block (`type`, `ctor`, `methods`, ...), with `-where` added for rules inside a `where { }` refinement. The predicate is the rule kind (`modifier`, `inherits`, `injects-only`, `using`, ...). `n` counts rules of the same kind in the same selector, in file order, starting at 0.

Ids come from structure, not line numbers, so reformatting, comments and moving a contract to another file do not change them. Two edits do: renaming the contract, and inserting a rule above another of the same kind (the later one is renumbered). `[TangentIgnore(Rule = "...")]` and `bind ... except <rule id>` refer to rules by id, so add new rules after existing ones of the same kind. `tangent lint --dump-ir` prints every id.

## 9. A complete example

```tangent
// repositories.tan
contract Repository {
    match: class in Infrastructure where name ends with "Repository"

    implements I{Name}
    interface I{Name} lives in Domain
    sealed

    ctor { injects only OrdersDbContext }
    methods where public { returns Task<*> }

    forbid using Orders.Api.**
}
```

For `OrderRepository` in `Orders.Infrastructure.Repositories`, this requires: a `sealed` class; an `IOrderRepository` interface that it implements, declared in the Domain layer; a constructor whose only parameters are `OrdersDbContext`; public methods that return `Task` or `Task<T>`; and no reference to anything in `Orders.Api`.
