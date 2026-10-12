# `Tangent.Core` public API

`Tangent.Core` is the Roslyn-free heart of Tangent. It lexes, parses and binds `.tan` files into an
immutable **rule IR** (`ArchitectureModel`). The analyzer, the CLI, the language server and the graph
exporter all read that IR and nothing else.

- Targets: `netstandard2.0` (what the compiler loads) and `net10.0`.
- Package references: `System.Memory`, `System.Collections.Immutable`, `PolySharp` — nothing else, ever.
- No `System.Text.Json`, no Roslyn, no regex engine. The binder never resolves C# type names.

Namespaces:

| Namespace | Contents |
|---|---|
| `Tangent.Core` | `ModelLoader`, `ModelCache` |
| `Tangent.Core.Text` | `StringTable`, `StringId`, `SourceFile`, `SourceSpan` |
| `Tangent.Core.Diagnostics` | `TangentDiagnostic`, `DiagnosticDescriptor`, `DiagnosticCatalog`, severities |
| `Tangent.Core.Syntax` | `Lexer`, `Token`, `TokenKind`, `Parser`, `Node`, `NodeList`, `SyntaxTree`, `SyntaxTreePrinter` |
| `Tangent.Core.Matching` | `Glob`, `GlobTable`, `TypePattern`, `TypePatternTable`, `AttributeGroup`, `Template` |
| `Tangent.Core.Ir` | `ArchitectureModel`, `Layer`, `Contract`, `Rule`, `Predicate`, `Selector`, `Condition`, `MatchClause`, `Binding`, `Ignore`, `LayerDependencyMatrix`, `IrText` |
| `Tangent.Core.Binding` | `Binder`, `BindResult` |
| `Tangent.Core.Serialization` | `IrJsonWriter` |

---

## 1. Loading a model

```csharp
using Tangent.Core;
using Tangent.Core.Binding;
using Tangent.Core.Text;

var files = new[]
{
    new SourceFile(".tangent/project.tan",     projectText),
    new SourceFile(".tangent/controllers.tan", controllersText),
};

BindResult result = ModelLoader.Load(files);          // ReadOnlySpan<SourceFile>
if (result.HasErrors) { /* report result.Diagnostics */ }
ArchitectureModel? model = result.Model;
```

`ModelLoader.Load` parses every file into **one compilation unit** sharing a single `StringTable`, then
binds them. Names are global across files and file order is irrelevant. `ModelLoader.ParseAll` stops after
parsing; `ModelLoader.ParseOne` parses a single file with a fresh string table (useful for an editor
buffer that is not yet part of a folder).

`BindResult`:

| Member | Meaning |
|---|---|
| `ArchitectureModel? Model` | The bound model. **Best effort**: a model is produced even when errors were reported, so the language server keeps working. |
| `ImmutableArray<TangentDiagnostic> Diagnostics` | Lex, parse and bind diagnostics merged, sorted by file index then offset then code. |
| `bool HasErrors` | True when any diagnostic has `DiagnosticSeverity.Error`. Do not enforce rules from a model with errors. |

`Binder.Bind(ImmutableArray<SyntaxTree>)` is the lower-level entry point. Every tree passed to it **must
share one `StringTable`** (that is what `ModelLoader.ParseAll` guarantees); it throws `ArgumentException`
otherwise, because `StringId`s from different tables are not comparable.

### Caching — `ModelCache`

Parsing `.tan` files per compilation would be catastrophic inside an analyzer. Cache by content hash:

```csharp
string key = string.Concat(additionalFiles.Select(f => f.GetText().GetChecksum().ToHex()));
BindResult result = ModelCache.GetOrAdd(key, () => ModelLoader.Load(sources));
```

`ModelCache` is a process-wide `ConcurrentDictionary<string, BindResult>`; the factory runs at most once
per key under normal races. `TryGet`, `Clear` and `Count` exist for tests and for the language server.
The key is entirely the caller's responsibility — Core never touches the filesystem.

---

## 2. Text primitives

### `StringId` / `StringTable`

Every identifier, glob, template, string literal and layer/contract name in a model is interned once.
Comparisons in the binder and in an evaluator are integer comparisons.

```csharp
StringId id = model.Strings.Intern("Orders.Api".AsSpan());   // allocates only on first sight
string text = model.Strings.GetString(id);                    // or model.Text(id)
ReadOnlySpan<char> span = model.Strings.GetSpan(id);          // no allocation
bool known = model.Strings.TryFind(name, out StringId found); // lookup without adding
```

`StringId.Invalid` (`Value == -1`) means "absent". `GetString(Invalid)` returns `""`, `GetSpan(Invalid)`
returns an empty span — neither throws.

### `SourceFile` and `SourceSpan`

`SourceFile(string Path, string Text)` is a readonly struct. `SourceSpan(int FileIndex, int Start,
int Length, int Line, int Column)` carries a 0-based file index into `ArchitectureModel.Files`, a 0-based
character offset and length, and the **1-based** line and column of its start.

```csharp
string? path = model.FilePathOf(rule.Span);       // null when the index is out of range
// Roslyn: Location.Create(path, new TextSpan(span.Start, span.Length), lineSpan)
```

---

## 3. Diagnostics

`TangentDiagnostic` is a readonly struct: `Code`, `Severity`, `Message`, `Span`, `FilePath`. Its
`ToString()` renders `path(line,col): severity CODE: message`.

`DiagnosticCatalog` holds **one** `DiagnosticDescriptor` per code from Appendix A of the plan, each with
an id, title, a `{0}`-style message template, a default severity, a `DiagnosticCategory` and, for rule
violations, the `RuleTier` it belongs to. The analyzer and the CLI must build their `DiagnosticDescriptor`s
from this catalogue rather than redefining strings.

```csharp
var diagnostic = TangentDiagnostic.Create(
    DiagnosticCatalog.ReturnsViolation, rule.Span, path,
    contractName, memberName, expected, actual);          // fills {0}..{3}

DiagnosticCatalog.TryGet("TNG0104", out DiagnosticDescriptor d);
foreach (var descriptor in DiagnosticCatalog.All) { /* register with Roslyn */ }
```

Codes produced **by Core**: `TNG1000` (lex/parse), `TNG1001`, `TNG1003`, `TNG1004`, `TNG1005`, `TNG1006`,
`TNG1010`, `TNG1011`, `TNG1030`.
Codes defined here but produced **by the analyzer/CLI**: `TNG0001`, `TNG0002`, `TNG01xx`, `TNG02xx`,
`TNG03xx`, `TNG1002` (needs the compilation), `TNG1020` (needs the compilation), `TNG9000`.

The plan's message templates use named holes (`{Contract}`); the catalogue converts them to positional
`{0}`-style holes so `string.Format` can fill them. `DiagnosticDescriptor.Format` never throws on an
argument-count mismatch — it returns the raw template.

---

## 4. Syntax: lexer, parser, AST

Front ends other than the DSL never need this layer; the language server and `tangent lint --dump-ast` do.

```csharp
SyntaxTree tree = Parser.Parse(file, fileIndex, strings);
string dump = SyntaxTreePrinter.Print(tree);      // tokens + AST, deterministic, LF line endings
```

`SyntaxTree` exposes `Source`, `FileIndex`, `Tokens` (`Token[]`, always ending in `TokenKind.EndOfFile`),
`Nodes` (`NodeList`), `Root` (the `NodeKind.File` node), `Strings` and `Diagnostics`.

`Token` is `readonly record struct Token(TokenKind Kind, int Start, int Length, int Line, int Column)`.
Text is recovered on demand with `token.Text(source)`; the lexer never substrings. `TokenKinds.Describe`,
`KeywordText`, `IsKeyword`, `IsModifier` and `IsMemberKind` support editors and error messages.

Lexical notes:

- Identifiers are ASCII `[A-Za-z_][A-Za-z0-9_]*`, may be dotted. A dotted name containing `*`/`**`
  segments lexes as one `Glob` token.
- `>` is always one token, so `Task<ActionResult<*>>` closes with two `GreaterThan`s.
- `{` directly attached to a name lexes the whole thing as one `Template` token (`I{Name}`,
  `X{Name:strip Suffix}`); a detached `{` is a block brace.
- Two keywords contain a hyphen: `null-checked`, `require-each`. A `-` not followed by `>` or a letter is
  an error token.
- `;` is accepted as an optional statement separator (the plan's own examples use it inside `fields { }`).
- Every lexical problem — unterminated string, unterminated block comment, bad escape, stray character —
  becomes a `TNG1000` diagnostic. The lexer never throws.

The AST is flat and index-based. `Node` is a readonly struct with `Kind`, `Span`, `FirstChild`,
`NextSibling`, `Payload`, `Payload2`; children are walked with `nodes.Children(index)` (a struct
enumerator), `FirstChild`, `NextSibling`, `ChildAt`, `ChildCount`. `NodeKind` documents what each payload
means. Payloads that hold names are raw `StringId.Value`s — wrap with `new StringId(payload)`.

Parser error recovery: on an unexpected token the parser records a `TNG1000` diagnostic phrased in domain
terms (`expected '{' after 'properties', found 'forbid'`), consumes the offending token, and then skips to
the next `}` at the current depth or to the next declaration/rule keyword. It never throws and always
produces a `File` node, so a half-typed buffer still yields a usable tree.

---

## 5. Matching primitives

### `Glob`

```csharp
Glob glob = model.Globs[layer.GlobIndex];
bool inLayer = glob.Matches(namespaceName.AsSpan());   // zero allocation
```

Compiled to an `ImmutableArray<GlobSegment>` of `Literal | Star | DoubleStar`; literals are interned.
Matching splits the candidate on `.` into a `stackalloc int[64]` (an `ArrayPool<int>` buffer above that)
and walks segments with a bounded backtrack for `**`.

`**` matches **zero or more** segments, so `Orders.Domain.**` matches `Orders.Domain`,
`Orders.Domain.Entities` and `Orders.Domain.Entities.Order`, but not `Orders.DomainX` or `Orders`.
`*` matches exactly one segment. Matching is ordinal and case-sensitive.

`GlobTable` compiles each distinct pattern once and hands out the indices stored in the IR.

### `TypePattern` / `TypePatternTable`

A predicate never stores a pattern directly; it stores a **group index**. A group is an alternation
(`A | B`) or, for `injects only`, the whole allowed list.

```csharp
TypePatternTable patterns = model.TypePatterns;
ImmutableArray<int> group = patterns.Group(rule.Predicate.Payload);

foreach (int patternIndex in group)
{
    TypePattern p = patterns.Patterns[patternIndex];
    if (p.IsWildcard) { /* `*` — matches any type */ continue; }

    ReadOnlySpan<char> name = patterns.Strings.GetSpan(p.Name);   // may be dotted
    if (p.TemplateIndex >= 0)
    {
        // The name is a template such as I{Name}: expand it against the bound type first.
        Template t = patterns.Templates[p.TemplateIndex];
    }

    for (int i = 0; i < p.Arity; i++)
    {
        int argument = patterns.Argument(p, i);   // == patterns.Arguments[p.ArgumentStart + i]
        // recurse against the symbol's i'th type argument
    }
}
```

Name matching rule, which an analyzer should reproduce against `INamedTypeSymbol`:

- a pattern name with **no dot** matches a type with that simple name **in any namespace**;
- a **dotted** pattern must match the namespace-qualified name as a suffix **at a segment boundary**
  (`Api.ApiControllerBase` matches `Orders.Api.ApiControllerBase`, not `Orders.XApi.ApiControllerBase`);
- arity must match exactly; a `*` argument matches any single type argument;
- a bare `*` pattern matches anything.

For tests, goldens and the CLI — anywhere Roslyn is not available — the table can match a C#-style
display string directly:

```csharp
patterns.MatchesGroup(groupIndex, "Task<ActionResult<OrderDto>>");   // true
patterns.Matches(patternIndex, displayName);
patterns.DescribeGroup(groupIndex);   // "Task<ActionResult<*>> | Task<IActionResult>"
```

A metadata arity suffix (`IRepository\`1`) is tolerated. Templated patterns do not match via the display
matcher — expand them first.

### `AttributeGroup`

```csharp
AttributeGroup group = model.AttributeGroups[rule.Predicate.Payload];
foreach (AttributeAlternative alternative in group.Alternatives)
{
    string name = model.Text(alternative.Name);
    bool anyArgs = alternative.AnyArgs;      // written as `[Route(*)]`
}

bool ok = group.Matches(attributeClass.Name.AsSpan());   // any alternative satisfies the group
```

Matching is by simple name, with or without the `Attribute` suffix on either side, and a dotted input is
reduced to its last segment. `AttributeGroup.NameEquals` exposes just the suffix rule.
`Describe()` renders `[HttpGet | HttpPost]`.

`attributes [A], [B]` means "an A **and** a B" and therefore compiles to **two rules**;
`attributes [A | B]` is one rule with a two-alternative group.

### `Template`

```csharp
Template t = model.Templates[rule.Predicate.Payload];    // for InterfaceLivesIn
string expected = t.Expand("OrderRepository", "Orders.Infrastructure");   // "IOrderRepository"

Span<char> buffer = stackalloc char[128];
if (t.Expand(typeName, ns, buffer, out int written)) { /* buffer[..written] */ }
else { /* `written` is the length required */ }
```

Holes: `{Name}` (simple type name), `{Namespace}` (containing namespace), `{Name:strip Suffix}` (simple
name with a trailing suffix removed if present). Unrecognised holes stay literal. `Parts` exposes the
compiled pieces (`Literal | Name | Namespace | NameStrip`) for anything that wants to render or analyse a
template; `IsLiteral` says the template expands to itself.

---

## 6. The IR

### `ArchitectureModel`

| Member | Notes |
|---|---|
| `StringId ProjectName`, `StringId Root` | From the `project` block; `Invalid` when absent. |
| `ImmutableArray<Layer> Layers` | Declaration order. `Layer(StringId Name, int GlobIndex, SourceSpan DeclarationSpan)`. |
| `ImmutableArray<Contract> Contracts` | Topologically ordered: a contract always follows the one it extends. |
| `ImmutableArray<Binding> Bindings` | `bind` statements. |
| `ImmutableArray<Ignore> Ignores` | `ignore` statements. |
| `LayerDependencyMatrix Dependencies` | Packed `ulong` bitsets. |
| `StringTable Strings` | Every interned name. |
| `TypePatternTable TypePatterns` | Patterns, their arguments and the alternation groups. |
| `ImmutableArray<Glob> Globs` | Side table indexed by layer/predicate payloads. |
| `ImmutableArray<AttributeGroup> AttributeGroups` | Side table. |
| `ImmutableArray<Template> Templates` | Side table. |
| `ImmutableArray<Condition> Conditions` | The flat condition forest. |
| `ImmutableArray<int> AllowedFrameworkGlobs` | Glob indices allowed from every layer. Always starts with `System.**`, then each `allow` glob. |
| `ImmutableArray<SourceFile> Files` | Indexed by `SourceSpan.FileIndex`. |

Helpers: `FindContract(name)`, `FindLayer(name)` (both return -1 when absent), `LayerOf(namespace)`,
`IsReferenceAllowed(fromLayer, namespace)`, `Text(StringId)`, `FilePathOf(SourceSpan)`.

The model is fully immutable after `Bind` returns and is safe to share across threads and compilations.

### Layer membership and the dependency matrix

```csharp
int layer = model.LayerOf(symbol.ContainingNamespace.ToDisplayString().AsSpan());
```

When several layer globs match, the one with the **most literal segments** wins, so a nested layer
(`Orders.Domain.Entities.**`) beats its enclosing layer (`Orders.Domain.**`). `-1` means the namespace is
outside every declared layer. Cache this per `INamespaceSymbol` — it repeats constantly.

```csharp
bool allowed = model.Dependencies.IsAllowed(fromLayer, toLayer);   // self is always allowed
ImmutableArray<int> targets = model.Dependencies.AllowedTargets(fromLayer);  // precomputed, includes self
ImmutableArray<int> extra = model.Dependencies.ExtraAllowedGlobs;  // same as model.AllowedFrameworkGlobs
```

`model.IsReferenceAllowed(fromLayer, ns)` folds the whole policy together: allowed if `ns` matches any
framework glob, or `ns` is outside every layer (not this model's business), or the matrix allows the
target layer. `Layer -> nothing` produces an empty row, so such a layer may only reference itself and
the framework globs.

### `Contract`

```csharp
public sealed class Contract
{
    StringId Name;                 // interned
    int ExtendsIndex;              // -1 when the contract extends nothing
    MatchClause? Match;            // null when only `bind` can apply it
    ImmutableArray<Rule> Rules;    // flattened: inherited rules first, then own
    SourceSpan DeclarationSpan;
}
```

`Rules` is already flattened over the `extends` chain. Every rule carries `OriginContract`, the index of
the contract it was written in, so `rule.OriginContract == contractIndex` identifies a contract's own
rules and anything else is inherited. Inherited rules keep the **originating contract's** rule id, so an
ignore by rule id keeps working through inheritance.

### `MatchClause`

```csharp
public readonly struct MatchClause
{
    TypeKind Kind;        // Any | Class | Interface | Record | Struct | Enum
    int LayerIndex;       // -1 when the source named a raw glob instead of a layer
    int GlobIndex;        // always valid: the layer's glob, or the raw glob
    int ConditionIndex;   // -1 when there is no `where`
    SourceSpan Span;
}
```

Evaluate as: the symbol's type kind matches `Kind`; its containing namespace matches
`model.Globs[GlobIndex]`; and `ConditionIndex` (if any) holds.

### `Rule`, `Selector`, `Predicate`

```csharp
public readonly struct Rule
{
    RuleId Id;               // stable string, see below
    Polarity Polarity;       // Require | Forbid
    Selector Selector;       // what it applies to
    Predicate Predicate;     // what is checked
    RuleTier Tier;           // Symbol | Reference | Flow
    SourceSpan Span;         // the whole `require ...` / `forbid ...` line
    int Specificity;         // refinement precedence
    int OriginContract;      // index into model.Contracts
}

public readonly struct Selector { MemberKind Kind; int ConditionIndex; }  // -1 = unconditional
public readonly struct Predicate { PredicateKind Kind; int Payload; int Payload2; }
```

`MemberKind` is `Type | Ctor | Method | Property | Field | Event | Nested`. `Type` means the bound type
itself (a top-level rule). `Selector` has value equality, so two rules target the same thing iff their
selectors are equal — conditions are structurally interned, so identical `where` clauses share an index.

**Specificity** — top-level type rules are `0`; a member block is `1`; a member block with `where` is `2`;
every nested refinement adds `1`. It orders refinements only. Contradictions at the same specificity are
a lint error (TNG1010), never resolved by precedence.

**Refinements** — a `where condition { ... }` block narrows the selector it sits in; it never introduces a
new `MemberKind`. A bare refinement at the top level of a contract keeps `Kind == MemberKind.Type` and
gets the condition's index, so its rules are evaluated against the bound type itself; nested inside a
member block (`methods { where public { ... } }`) it keeps that block's `MemberKind` and `and`s its
condition onto the block's own. Each level adds one to `Specificity`. A refinement's `where` is **not**
the `match` clause's `where`: `match: class in Api` followed by `where name ends with "X" { sealed }`
binds the condition to the refinement, because only a refinement's condition is followed by `{`.

**Tiers** — `Symbol` for `Modifier`, `Inherits`, `Implements`, `ImplementsOnly`, `InheritsOnly`,
`HasAttributes`, `Returns`, `Injects`, `InjectsOnly`, `Accessor`, `HasMemberKind`, `InterfaceLivesIn`,
`NameIs`, `LivesIn`; `Reference` for `UsingNamespace`, `UsingContract` and `NewExpression`; `Flow` for `EachParamAssignedToField`, `EachParamNullChecked`, `EachParamNotStored` and
`Stateless`. Register operation actions only when a bound type has rules above `Symbol`.

**Predicate payloads**, switched on `PredicateKind`:

| Kind | `Payload` | `Payload2` |
|---|---|---|
| `Modifier` | `Modifiers` mask | — |
| `Inherits`, `Implements`, `Returns`, `Injects`, `InjectsOnly`, `ImplementsOnly`, `InheritsOnly`, `NewExpression` | type-pattern **group** index | — |
| `HasAttributes` | attribute-group index | — |
| `UsingNamespace` | glob index | — |
| `UsingContract` | contract index | — |
| `NameIs` | `NameOperator` (`StartsWith`/`EndsWith`/`Contains`/`Matches`) | `StringId` of the operand |
| `LivesIn` | layer index, or -1 | glob index, or -1 |
| `Accessor` | `AccessorKind` (`Get`/`Set`/`Init`) | `Modifiers` mask (`forbid public set`) |
| `InterfaceLivesIn` | template index | layer index (-1 if the layer was undeclared) |
| `HasMemberKind` | `MemberKind` | `Modifiers` mask (`forbid public fields`) |
| `EachParamAssignedToField` | `Modifiers` mask for the field (`private readonly`) | — |
| `EachParamNullChecked`, `EachParamNotStored`, `Stateless` | — | — |

`ConditionKind` covers `And`, `Or`, `Not` (Left is the negated condition), the four name tests,
`Inherits`, `Implements`, `HasAttributes` and `Modifier`.

`Modifiers` is a `[Flags]` enum covering `public private protected internal static sealed abstract
readonly partial class record struct interface`, plus `AccessibilityMask` and `TypeKindMask`.

Semantics worth repeating: `injects only A, B` means every constructor parameter type must match one of
the group; `injects A` means A must be **among** the parameters; a comma list elsewhere (`attributes [A],
[B]`, `using X.**, Y.**`, `new A, new B`) is a conjunction and compiles to one rule per element.

### `Condition`

Conditions live in one flat array so the tree costs no object graph.

```csharp
void Evaluate(ArchitectureModel model, int index, INamedTypeSymbol symbol)
{
    if (index < 0) return true;                 // unconditional
    Condition c = model.Conditions[index];
    switch (c.Kind)
    {
        case ConditionKind.And:  return Evaluate(model, c.Left, symbol) && Evaluate(model, c.Right, symbol);
        case ConditionKind.Or:   return Evaluate(model, c.Left, symbol) || Evaluate(model, c.Right, symbol);
        case ConditionKind.NameStartsWith:  // c.Payload is a StringId.Value
        case ConditionKind.NameEndsWith:
        case ConditionKind.NameContains:
        case ConditionKind.NameMatches:      // regular expression source; discouraged
            ...
        case ConditionKind.Inherits:         // c.Payload is a type-pattern group index
        case ConditionKind.Implements:
        case ConditionKind.HasAttributes:    // c.Payload is an attribute-group index
        case ConditionKind.Modifier:         // c.Payload is a Modifiers mask (`methods where public`)
            ...
    }
}
```

`Left`/`Right` are condition indices for `And`/`Or` and `-1` for leaves. `and` binds tighter than `or`.
`IrText.DescribeCondition(model, index)` renders a condition back to source-like text.

### `Binding` and `Ignore`

```csharp
public sealed class Binding
{
    StringId TypeName;                          // fully qualified, exactly as written
    ImmutableArray<int> ContractIndices;        // resolved; unknown names were reported as TNG1001
    ImmutableArray<StringId> ExcludedAttributes;// `except [ProducesResponseType]`
    ImmutableArray<StringId> ExcludedRules;     // `except "Controller/type/modifier#0"`
    SourceSpan Span;
}

public sealed class Ignore
{
    StringId TypeName;
    ImmutableArray<int> ContractIndices;        // empty = every contract
    bool AllContracts;                          // == ContractIndices.IsDefaultOrEmpty
    StringId Reason;                            // required by the grammar
    SourceSpan Span;
}
```

Core does not resolve `TypeName` against a compilation — compare it to the symbol's fully qualified
metadata name yourself and report `TNG1002` when nothing matches. An `except` entry is either an
attribute simple name (skip rules whose `HasAttributes` group matches that name) or a rule id string
(skip the rule with that `Rule.Id`); report `TNG0002` for a rule id that no bound contract defines.

Binding precedence per the plan is: `[Tangent("X")]` attribute, then `bind` statements, then each
contract's `match` clause. Core supplies the second and third; the attribute is the analyzer's job.

### Rule ids

```
<Contract>/<selector>/<predicate-kind>#<ordinal>
Controller/ctor/injects-only#0
Controller/methods/attributes#1
Entity/type/using#0
Entity/type-where/member-kind#0
```

- `<Contract>` is the contract the rule was **written in** (so inherited rules keep the parent's id).
- `<selector>` is `type | ctor | methods | properties | fields | events | nested`, with `-where`
  appended for a rule written inside a `where { }` refinement (`Entity/type-where/member-kind#0`), so a
  refined rule never shares an id with the plain rule it narrows. A member block's own `where`
  (`methods where public { ... }`) is part of the block, not a refinement, and does not add the suffix.
- `<predicate-kind>` is the slug from `IrText.PredicateSlug`: `modifier`, `inherits`, `implements`,
  `attributes`, `returns`, `injects`, `injects-only`, `using`, `new`, `accessor`, `interface-lives-in`,
  `member-kind`, `each-param-assigned-to-field`, `each-param-null-checked`, `each-param-not-stored`,
  `stateless`.
- `<ordinal>` counts rules of that (contract, selector, predicate kind) in declaration order.

Ids are derived from structure, never from source position, so reformatting, adding comments or moving a
rule between lines leaves them unchanged — which is what makes `except "..."` and
`[TangentIgnore(Rule = "...")]` durable. A golden test locks this in.

### `IrText`

Renders IR back to DSL-like text, used by diagnostics, the JSON writer and the CLI:
`PolarityName`, `TierName`, `TypeKindName`, `AccessorName`, `MemberKindSlug`, `PredicateSlug`,
`ModifierText`, `DescribeSelector`, `DescribeCondition`, `DescribePredicate`, `DescribeRule`,
`DescribeScopedRule` (a rule inside its member block or `where { }` refinement), `DescribeMatch`,
`DescribeLayerDependencies` (a layer's `->` line) and `DescribeAllowList` (the effective `allow` list).
`tangent check --explain` is built on the last four.

---

## 7. Serialisation

```csharp
string json = IrJsonWriter.Write(result.Model);   // "null\n" for a null model
```

Hand-written with a `StringBuilder`, two-space indented, LF line endings, deterministic (declaration
order throughout). Shape:

```jsonc
{
  "project": "Orders",
  "root": "src/",
  "layers": [
    { "name": "Api", "namespace": "Orders.Api.**", "allowedDependencies": ["Application", "Domain"] }
  ],
  "allowedFrameworkNamespaces": ["System.**", "Microsoft.Extensions.**"],
  "contracts": [
    {
      "name": "AggregateRoot",
      "extends": "Entity",
      "file": "entities.tan", "line": 15,
      "match": "class in Orders.Domain.Entities.** where inherits AggregateRoot<*>",
      "rules": [
        { "id": "Entity/type/inherits#0", "origin": "Entity", "polarity": "require",
          "selector": "type", "predicateKind": "inherits",
          "predicate": "inherits Entity<*> | AggregateRoot<*>", "tier": "symbol", "specificity": 0 }
      ]
    }
  ],
  "bindings":   [ { "type": "...", "contracts": ["Controller"], "exceptAttributes": ["ProducesResponseType"], "exceptRules": [] } ],
  "ignores": [ { "type": "...", "contracts": [], "allContracts": true, "reason": "migrating in Q4 2026" } ]
}
```

This is what `tangent lint --dump-ir` prints and what the golden tests compare.

---

## 8. What the binder checks

`Binder.Bind` follows §8.3 of the plan:

1. Collect `project`, `layer`, `contract`, `bind`, `ignore` across every file. Duplicate names →
   **TNG1003**.
2. Compile layer globs; resolve `->` lines. An undeclared layer name → **TNG1005**. A cycle in the
   dependency graph → **TNG1006** (warning). `allow` globs plus the implicit `System.**` become
   `AllowedFrameworkGlobs`.
3. Resolve `extends` (unknown parent → **TNG1001**), detect cycles → **TNG1004** (the offending link is
   cut so flattening terminates), then order contracts topologically.
4. Walk each contract body, compile patterns/globs/attribute groups/templates/conditions, assign
   selectors, specificity, tiers and rule ids, and flatten parent rules in front of own rules.
5. Resolve `bind`/`ignore` contract names → **TNG1001** when unknown.
6. Post-checks over the finished model:
   - **TNG1011** — an own rule that loosens an inherited one: same selector with the opposite polarity on
     the same predicate, or two `require`d modifier sets that contradict.
   - **TNG1010** — two rules of the **same** origin contract with the same selector and the same
     specificity that contradict (same predicate opposite polarity; `require public` + `require private`;
     `require sealed` + `require abstract`; conflicting type-kind keywords).
   - **TNG1030** — a contract with no `match` clause, no `bind` naming it, and no matching child.

What the binder deliberately does **not** do: resolve C# type names. The same `.tangent` folder is shared
by projects that see different assemblies, so `inherits ApiControllerBase` stays a pattern until the
analyzer evaluates it (and reports **TNG1020** if it cannot be resolved).

---

## 9. Performance contract

Anything in Core that an analyzer calls per symbol must stay allocation-free:

- `Glob.Matches`, `TypePatternTable.Matches`, `AttributeGroup.Matches`, `Template.Expand(span)`,
  `StringTable.GetSpan`/`TryFind` and `ArchitectureModel.LayerOf`/`IsReferenceAllowed` allocate nothing.
- The lexer, the glob matcher and the type-pattern matcher use no LINQ, closures or `params`.
  `stackalloc` is used for at most 64 elements with an `ArrayPool<T>` fallback above that.
- `SearchValues<char>` is used for identifier scanning on `net10.0` behind `#if NET10_0_OR_GREATER`, with
  an equivalent hand-rolled loop on `netstandard2.0`. Both paths accept exactly the same characters, so
  the two targets tokenise identically.
- The binder and `IrJsonWriter` are one-time cold paths and may allocate.
- `Rule`, `Predicate`, `Selector`, `Condition`, `Token`, `Node`, `TypePattern`, `GlobSegment`,
  `TemplatePart`, `StringId`, `SourceSpan` and `SourceFile` are `readonly struct`s held in arrays.
  Only the top-level containers are classes.

---

## 10. Golden tests

`tests/Tangent.Core.Tests/cases/` holds one `.tan` file (or one directory of them, bound together) per
case with siblings `<name>.tokens.txt`, `<name>.ast.txt`, `<name>.ir.json` and `<name>.diagnostics.txt`.
Run with `TANGENT_UPDATE_GOLDENS=1` to rewrite them after an intentional change:

```powershell
$env:TANGENT_UPDATE_GOLDENS='1'; dotnet test tests/Tangent.Core.Tests
```

The cases cover every reference example in §5.4 of the plan (individually and as one multi-file model)
plus malformed inputs that lock in error recovery: missing braces, an unknown keyword, an unterminated
string, an undeclared layer, an `extends` cycle, conflicting rules, a loosening child, a duplicate
contract and an unknown contract in `bind`/`ignore`.
