---
name: tangent
description: Write and maintain Tangent contracts, the `.tan` files that describe and enforce the architecture of a .NET solution (layers, allowed dependencies, and rules for controllers, handlers, repositories, entities and services). Use this skill whenever the user wants to adopt Tangent, create or change a `.tangent/` folder, add or tighten an architecture rule, turn an architecture decision, ADR or convention into enforceable rules, enforce layering or clean architecture in C#, or review what existing contracts say. Also use it when a task mentions `.tan` files, `tangent lint`, `tangent check`, or a TNG diagnostic, even if the user does not say "contract".
---

# Writing Tangent contracts

Tangent turns an architecture into rules a compiler checks. The rules live in `.tan` files in a `.tangent/` folder at the solution root. A Roslyn analyzer reports every violation in the IDE and in `dotnet build`, and the `tangent` CLI reports them from the command line and in CI. Every violation message names the rule it broke and where that rule is declared, so once good contracts exist, anyone writing code (a person or an agent) is told exactly what to fix.

That makes the contracts the part that needs care. Your job with this skill is to write contracts that state the architecture the team actually wants: precise enough to catch real mistakes, and not so broad that they flag code the team considers fine.

`references/dsl.md` is the full language. Read it before you write your first rule in a session; the syntax is small, but several details (what `->` allows, what `allow` switches on, how `match` binds) change what a contract means.

## The shape of a model

```tangent
// project.tan: the layers and which way dependencies may flow
project "Orders" {
    root: "src/"
    layer Api            = Orders.Api.**
    layer Application    = Orders.Application.**
    layer Domain         = Orders.Domain.**
    layer Infrastructure = Orders.Infrastructure.**

    Api            -> Application, Domain
    Application    -> Domain
    Infrastructure -> Application, Domain
    Domain         -> nothing
}

// handlers.tan: one contract per kind of type
contract Handler {
    match: class in Application where name ends with "Handler"

    implements IRequestHandler<*, *>
    sealed
    ctor { injects only IOrderRepository, IClock }
    methods where public { returns Task<*> }
    forbid using Orders.Infrastructure.**
}
```

- A **layer** names a namespace glob. The `->` lines list what each layer may reference; everything else is forbidden.
- A **contract** describes one kind of type. Its `match:` clause binds types by convention, so new types are covered without anyone marking them.
- A **rule** is `require` or `forbid` plus a predicate. Rules inside `ctor { }`, `methods { }`, `properties { }` and the other member blocks apply to each member of that kind.

## Creating contracts for a solution

Work from what the code already does. A contract that matches the existing conventions finds the real exceptions; a contract invented from a textbook flags half the solution and gets deleted.

1. **Survey the solution.** List the projects and their root namespaces. Look at how types are named and where they live: do handlers end in `Handler`, repositories in `Repository`? Which base classes and interfaces recur (`ControllerBase`, `IRequestHandler<,>`, `Entity<T>`)? Which interfaces sit in which project? What do constructors typically inject? Which external packages does each project reference? A few `grep`s for `namespace`, `class .* : ` and constructor signatures answer most of this.
2. **Scaffold if there is nothing yet.** `tangent init` writes `.tangent/project.tan` with one layer per root namespace, all set to `-> nothing`, plus an empty `contracts.tan`. Starting closed is deliberate: the first check shows the real dependency graph, and each `->` you add is a decision.
3. **Write `project.tan`.** Name the layers, then write the `->` lines for the direction the team intends, which may be stricter than what the code does today. Decide on `allow` deliberately: without it, external packages are not checked at all; with it, every external namespace must be listed (see `references/dsl.md`, section 2).
4. **Write one contract per kind of type**, in its own file (`controllers.tan`, `handlers.tan`, ...). For each:
   - write the `match:` from the convention you found, as narrow as it needs to be (`class in Api where name ends with "Controller"`, not `any in Api`);
   - add the rules that encode decisions the team made. Each rule should be something a reviewer would reject a pull request for. Five meaningful rules beat twenty speculative ones;
   - put a one-line comment above the contract, and above any rule whose reason is not obvious, saying *why*. The rule text says what; the comment is what a future reader needs to decide whether the rule still holds.
   `references/patterns.md` has tested contracts for the common kinds of type.
5. **Validate the model.** `tangent lint .tangent` parses and binds the `.tan` files in seconds, without loading the solution. `tangent lint` from the solution folder also checks that every C# type name the contracts mention exists (TNG1020). Fix every TNG10xx it reports. Two to know: TNG1020 means a type name in a rule does not resolve, often because the projects the contract covers do not reference that package (`forbid new DbContext` over a layer without EF Core); forbid the namespace or `using contract` instead. TNG1030 means a contract has no `match` and no `bind` at all. A `match` that selects no types is **not** reported, which is why step 6 matters.
6. **Check what each contract binds.** Nothing else tells you when a `match` selects no types, or the wrong ones. Run `tangent graph --include-unbound -o graph.json` and read `nodes[].contracts`: every type that should be under a contract should list it, nothing else should, and the types with an empty list are the ones no contract covers. A match that is too wide is the most common authoring mistake, and the check output alone will not show it.
7. **Run `tangent check --explain` and triage with the user.** Every violation is one of two things: real architectural debt, or a rule that is wrong or too broad. Present the violations grouped by rule, say which you think each is, and let the user decide. Do not tune rules until the count reaches zero: a contract loosened to fit the code enforces nothing.

## Changing existing contracts

- **Tightening** (a new rule, a narrower `injects only`, a new `forbid`) is what the user asked for; do it, then run the check and report what it now catches.
- **Loosening** (removing a rule, widening a list, adding a `->` target or an `allow` glob, adding an `ignore`) changes what the architecture permits. Do it only when the user asked for that specific change, and say plainly what is now allowed that was not before.
- **Keep rule ids stable.** A rule id is `Contract/selector/predicate#n`, where `n` counts rules of the same predicate kind in the same selector, in file order. Inserting a rule above another of the same kind renumbers the later one, and any `[TangentIgnore(Rule = "...")]` or `bind ... except` that names the old id stops matching. Add new rules after existing rules of the same kind.
- **Use `extends` for a stricter variant** (`contract AggregateRoot extends Entity`). A child may only add rules; loosening an inherited rule is TNG1011.
- **Ignores** (`ignore Type [from Contract] reason "..."`) are for types that are deliberately different. Every one needs a real reason and is reported. Prefer a narrower `match` when a whole group of types should not be under a contract.

## When code, not contracts, is the task

If the user asks you to write or fix C# in a solution that has contracts, the checker is your feedback loop: run `tangent check --explain` after your change and fix what it reports in the code. Each violation shows the rule as written, where it is declared, and the offending line. Do not edit `.tangent/` to make your own change pass; a contract change is a separate decision for the user. `references/diagnostics.md` lists every code and the usual code fix.

## Commands

| Command | Use |
|---|---|
| `tangent init` | Scaffold `.tangent/` from the solution's projects. |
| `tangent lint [.tangent]` | Parse and bind the `.tan` files; reports TNG10xx. Pointed at `.tangent` it skips loading the solution. |
| `tangent check --explain` | Run every contract over the solution; show each violation with its rule. `--json out.json` for machine-readable output, `--project Name` for one project. |
| `tangent graph --include-unbound -o graph.json` | Export layers, contracts, every type and its bindings, for checking coverage or for the viewer. |
| `tangent diff [--apply]` | Show, or write, mechanical fixes for structural violations. |

Run them from the solution folder. If `tangent` is not on the path, try `dotnet tangent` (a local tool; `dotnet tool restore` first).

## Reference files

- `references/dsl.md`: the complete language: syntax, layers and `allow`, contracts and `extends`, selectors, every rule form, binding, ignores. Read before writing rules.
- `references/patterns.md`: tested contracts for controllers, handlers, repositories, entities, domain services, view models and layering, with notes on when to adapt them.
- `references/diagnostics.md`: every TNG code, what triggers it, and the usual fix. Use it to explain violations to the user.
