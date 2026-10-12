# Tangent diagnostics and their usual fixes

Every violation message ends with `(rule <rule id>, <file>.tan:<line>)`. Run `tangent check --explain` to see the rule text under each one. Use this file to explain violations to the user and to judge whether a violation is real debt or a sign that a rule is wrong. The usual fixes for TNG0xxx change C# code; TNG1xxx are problems in the contracts themselves.

## Contents

- Binding: TNG0001, TNG0002
- Structural (symbol): TNG0101–TNG0113
- Reference and layers: TNG0201–TNG0205
- Flow: TNG0301–TNG0303
- Model: TNG1xxx
- Ignored violations: TNG9000

## Binding

| Code | Means | Usual fix |
|---|---|---|
| TNG0001 | `[Tangent("X")]` names a contract that does not exist. Error. | Fix the name; the message suggests the closest. Prefer `Contracts.X` constants when the analyzer package is referenced. |
| TNG0002 | `[TangentIgnore(Rule = "...")]` names a rule id that does not exist. Error. | Tell the user; the ignore is stale or misspelt. |

## Structural (symbol)

| Code | Means | Usual fix |
|---|---|---|
| TNG0101 | A required modifier is missing or a forbidden one is present (`sealed`, `public`, `static`, `readonly`, `abstract`, `record`, ...). | Add or remove the modifier. `tangent diff` often has a mechanical fix. |
| TNG0102 | No base type matches `inherits`. | Derive from the required base type. |
| TNG0103 | No interface matches `implements`. With `I{Name}`, the interface named after the type is missing. | Implement it; create the interface if needed, in the layer that `interface I{Name} lives in` names. |
| TNG0104 | A required attribute is missing. | Add it. `[A], [B]` needs both; `[A \| B]` needs one. |
| TNG0105 | The return type does not match `returns`, or matches a `forbid returns`. | Change the signature, for example return `Task<ActionResult<T>>`, or return a result instead of `void`. |
| TNG0106 | A constructor parameter type is not in `injects only`, or a type required by `injects` is missing. | Depend only on the listed types. Put extra behaviour behind one of them, for example send a request through `IMediator` instead of injecting a service. |
| TNG0107 | A property has a forbidden accessor, usually a public setter. | Use `init`, a private setter, or a constructor. |
| TNG0108 | The type declares a kind of member it must not have, such as public fields or nested types. | Turn public fields into properties; move nested types out. |
| TNG0109 | The interface named by `interface I{Name} lives in <layer>` is in another layer. | Move the interface to that layer's namespace. |
| TNG0110 | A directly implemented interface is not in `implements only`. | Remove the extra interface, or move that behaviour to another type. |
| TNG0111 | The base type is not in `inherits only`. | Derive from an allowed base type. |
| TNG0112 | A type or member name fails the naming rule (`ends with "Handler"`, `ends with "Async"`, ...). | Rename it, and update its references. |
| TNG0113 | The type is not in the layer or namespace that `lives in` requires. | Move it: change the namespace (and usually the folder). |

## Reference and layers

| Code | Means | Usual fix |
|---|---|---|
| TNG0201 | The type references something in a namespace that `forbid using` forbids. Signatures, attributes and bodies all count. | Depend on an abstraction in an allowed layer instead. |
| TNG0202 | A layer references a layer that its `->` line does not allow. | Do not reach across. Define an interface in the inner layer (Domain or Application), implement it in the outer layer, and inject the interface. Adding a `->` target instead is a decision about the architecture, for the user. |
| TNG0203 | `new` of a type that `forbid new` forbids, or of a type derived from it. | Inject the dependency (or a factory) instead of creating it. |
| TNG0204 | A reference to a type bound to a contract that `forbid using contract` forbids. | Go through the allowed path, for example a handler instead of a repository from a controller. |
| TNG0205 | A reference to an external namespace that the project's `allow` list does not include. | Remove the dependency, or wrap it behind an interface in a layer that may use it. Adding an `allow` glob is the user's decision. |

## Flow (constructor bodies)

| Code | Means | Usual fix |
|---|---|---|
| TNG0301 | A constructor parameter is not assigned to a `private readonly` field. With `each parameter not stored`, the opposite: a parameter was stored. | Assign each parameter to a `private readonly` field, or stop storing it, as the rule says. |
| TNG0302 | A constructor parameter is not null-checked. | Add `ArgumentNullException.ThrowIfNull(p)`, `p ?? throw new ArgumentNullException(nameof(p))`, or `if (p is null) throw ...`. |
| TNG0303 | A `stateless` type has a mutable instance field or a property with a setter. | Make fields `readonly`, remove setters, or move the state out. |

## Model (`.tan` files)

TNG1000–TNG1099 are problems in the contracts themselves: parse errors, unknown names, cycles, conflicting rules, a child contract that loosens its parent, a type name that does not resolve in the projects the contract covers (TNG1020, a warning), a contract with no `match` and no `bind` (TNG1030, a warning). A TNG10xx **error** makes `tangent check` exit with code 2, and then **no code was checked**. When you are writing contracts, fix them. When you are writing code, report them to the user.

## Ignored violations

TNG9000 (hidden) records that an ignore was applied: from `[TangentIgnore]`, an `ignore` line, or `bind ... except`. `tangent check --show-ignored` lists them. It is information, not a problem to fix.
