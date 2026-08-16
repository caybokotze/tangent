# Tangent diagnostics

All Tangent diagnostics use the `TNG` prefix. Severities are defaults; map them in `.editorconfig` like any analyzer:

```ini
[*.cs]
dotnet_diagnostic.TNG0202.severity = error   # layer violations fail the build
dotnet_diagnostic.TNG0104.severity = suggestion
```

| Range | Meaning | Default severity |
|---|---|---|
| TNG0001–0099 | Binding errors (unknown contract, unknown rule id in exemption) | Error |
| TNG0100–0199 | Structural violations (Tier 0, symbol) | Warning |
| TNG0200–0299 | Reference and layer violations (Tier 1, reference) | Warning |
| TNG0300–0399 | Flow violations (Tier 2, operation tree) | Warning |
| TNG1000–1099 | `.tan` model problems (parse errors, conflicts, cycles) | Error |
| TNG9000 | Exemption applied | Hidden |

Every violation message has one shape:

```
{Contract}: {member} {what was expected}, found {what exists}. (rule {RuleId}, {file}:{line})
```

## Binding

### TNG0001
**Contract '{0}' is not defined.** A `[Tangent("X")]` attribute names a contract that does not exist in the loaded `.tan` files. The message suggests the closest declared name. Error.

### TNG0002
**Rule '{0}' referenced in exemption does not exist.** A `[TangentExempt(Rule = "...")]` names a rule id that no contract declares. Error.

## Structural (Tier 0)

### TNG0101
**{Contract}: type must be {modifier}.** A `require`/`forbid` modifier rule (`sealed`, `public`, `readonly`, `static`, `partial`, `abstract`, `class`, `record`, ...) failed on the type or on a member selected by a member block.

### TNG0102
**{Contract}: type must inherit {pattern}.** No base type in the chain matches the type pattern (alternation `A | B` allowed).

### TNG0103
**{Contract}: type must implement {pattern}.** No implemented interface matches the pattern. Templates such as `I{Name}` are expanded per bound type.

### TNG0104
**{Contract}: {member} must have attribute {attr}.** `attributes [A], [B]` requires both; `attributes [A | B]` requires either; `[A(*)]` requires at least one `A` with any arguments.

### TNG0105
**{Contract}: {member} must return {pattern}, found {actual}.**

### TNG0106
**{Contract}: constructor may only inject {list}; found {actual}.** `injects only` lists the allowed constructor parameter types. `injects X` (no `only`) requires `X` to be among them.

### TNG0107
**{Contract}: property {name} must not have a public setter.** Accessor rules: `forbid public set`, `require get`, `require init`.

### TNG0108
**{Contract}: type must not declare {memberKind}.** Member-kind presence rules: `forbid public fields`, `forbid static methods`, `forbid nested`.

### TNG0109
**{Contract}: interface {name} must live in {layer}.** `interface I{Name} lives in Domain` found the interface in another layer.

## Reference (Tier 1)

### TNG0201
**{Contract}: reference to {symbol} in forbidden namespace {glob}.** `forbid using Orders.Infrastructure.**`. Any symbol reference in the type resolves into the namespace: base types, attributes, member signatures, and bodies.

### TNG0202
**Layer {from} may not depend on layer {to} ({symbol}).** Synthesised from the `project` dependency matrix. `System.**` and the project's `allow` globs are implicitly permitted. Namespaces that belong to no layer are permitted.

### TNG0203
**{Contract}: forbidden object creation: new {type}.** `forbid new DbContext, new HttpClient`. Derived types count: `new OrdersDbContext()` violates `forbid new DbContext`.

## Flow (Tier 2)

### TNG0301
**{Contract}: constructor parameter {name} is not assigned to a private readonly field.** From `require each parameter assigned to private readonly field`.

### TNG0302
**{Contract}: constructor parameter {name} is not null-checked.** From `require each parameter null-checked`. Recognised forms: `ArgumentNullException.ThrowIfNull(p)`, `p ?? throw ...`, `if (p is null) throw ...`.

### TNG0303
**{Contract}: type must be stateless; mutable instance field {name}.** From `stateless`. Instance fields must be readonly and instance properties must not have setters.

## Model (`.tan` files)

| Code | Message | Severity |
|---|---|---|
| TNG1000 | Parse error: {0} | Error |
| TNG1001 | Unknown contract '{0}' in bind/exempt/extends | Error |
| TNG1002 | Unknown type '{0}' in bind/exempt | Error |
| TNG1003 | Duplicate declaration '{0}' | Error |
| TNG1004 | Contract inheritance cycle: {chain} | Error |
| TNG1005 | Undeclared layer '{0}' | Error |
| TNG1006 | Layer dependency cycle: {chain} | Warning |
| TNG1010 | Conflicting rules at same specificity: {a} vs {b} | Error |
| TNG1011 | Contract '{child}' loosens rule {id} inherited from '{parent}' | Error |
| TNG1020 | Type '{0}' in contract '{1}' could not be resolved | Warning (error with `tangent lint --strict`) |
| TNG1030 | Contract '{0}' is never bound | Warning |

## Exemptions

### TNG9000
**Exemption applied: {reason}.** Hidden by default. `tangent check --show-exemptions` lists them. Sources: `[TangentExempt("reason")]` on a type or member (optionally `Contract = "..."` or `Rule = "..."`), `exempt Type [from Contract] reason "..."` statements, and `bind Type to Contract except [Attr]`.
