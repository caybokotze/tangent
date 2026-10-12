# `Tangent.LanguageServer`

The LSP server for `.tan` files. It is built on `OmniSharp.Extensions.LanguageServer` 0.19.9, reads
`Tangent.Core` for everything about the DSL, and lazily opens the workspace's C# solution with
`MSBuildWorkspace` for the features that need real type names.

Everything in this document works **without** a C# compilation. The compilation only ever adds
information; it is never required, never awaited on the request path, and its failure is logged
rather than surfaced.

---

## 1. Hosting

```csharp
namespace Tangent.LanguageServer;

public static class TangentLanguageServerHost
{
    public static Task RunAsync(Stream input, Stream output, string? workspaceRoot, CancellationToken cancellationToken);
}
```

`tangent serve` calls this with `Console.OpenStandardInput()` / `Console.OpenStandardOutput()`:

```csharp
await TangentLanguageServerHost.RunAsync(
    Console.OpenStandardInput(),
    Console.OpenStandardOutput(),
    Environment.CurrentDirectory,   // or null to use rootUri from initialize
    cancellationToken);
```

The task completes when the client sends `exit`, or when `cancellationToken` fires (which forces a
shutdown). `workspaceRoot` may be `null`; the server then uses `workspaceFolders`, `rootUri` or
`rootPath` from `initialize`, and failing all three, the nearest `.tangent` ancestor of the first
`.tan` file the client opens.

The VS Code client in `editors/vscode` starts the server as `dotnet tangent serve` over stdio and
registers it for the `tangent` and `csharp` languages. C# documents are ignored by every handler;
they are only there so the extension can send the custom request below.

### Finding the `.tangent` folder

1. `<root>/.tangent` if it exists.
2. Otherwise a breadth-first search under `<root>` for a directory named `.tangent`, skipping
   dot-directories, `bin`, `obj`, `node_modules` and `artifacts`, and capped at 2000 directories.
3. Otherwise the nearest ancestor of an opened `.tan` file that is named `.tangent`.

Every `*.tan` file under the folder (recursively) is part of the model, whether or not the editor
has it open.

---

## 2. Capabilities

| Capability | Notes |
| --- | --- |
| `textDocumentSync` | Full text. Open buffers override the file on disk; `didClose` falls back to disk. |
| `publishDiagnostics` | Published for **every** file in the folder on every change, not only open ones. |
| `semanticTokensProvider` | `full` and `range`. Legend below. |
| `completionProvider` | Trigger characters `" "`, `"["`, `"."`, `","`. No resolve. |
| `hoverProvider` | Markdown. |
| `definitionProvider` | `.tan` → `.tan`, and `.tan` → `.cs` for type patterns. |
| `referencesProvider` | Contract and layer names. |
| `tangent/contractDefinition` | Custom request; see §4. |

### Diagnostics

The whole folder is re-parsed and re-bound on every change, debounced by 150 ms. Binding a handful
of `.tan` files costs microseconds, and rebinding everything is the only way to get cross-file
diagnostics right: deleting `contract Entity` in `entities.tan` must light up `ignore … from Entity`
in `bindings.tan`, which nobody has open.

- **TNG1000** — lex and parse errors, phrased in domain terms by Core's parser.
- **TNG1001, TNG1003–TNG1006, TNG1010, TNG1011, TNG1030** — binder diagnostics.
- **TNG1020** — a type pattern that no type in the compilation can satisfy. Only reported once the
  compilation has loaded; when it finishes loading, diagnostics are republished so TNG1020 appears
  without the user having to type anything. C# keyword type names (`void`, `string`, `int`, …) are
  resolved through their framework names, so `forbid returns void` is not flagged.

`Hidden` diagnostics are dropped. Severities map `Error → 1`, `Warning → 2`, `Info → 3`,
`Hidden → 4`. Every diagnostic carries `source: "tangent"` and its `TNGxxxx` id as `code`.

### Semantic tokens

The legend is fixed:

```
tokenTypes:     keyword, type, namespace, class, variable, string, comment, operator, decorator
tokenModifiers: declaration, defaultLibrary
```

| Construct | Token type | Modifiers |
| --- | --- | --- |
| Reserved words (`contract`, `require`, `methods`, `sealed`, …) | `keyword` | |
| `->`, `\|`, `,`, `=`, `:`, `<`, `>`, `*`, `**` | `operator` | |
| `"..."` | `string` | |
| `//` and `/* */` | `comment` | |
| `contract X` | `class` | `declaration` |
| `extends X`, `bind … to X`, `ignore … from X` | `class` | |
| `layer X = …` | `namespace` | `declaration` |
| Layer references (`in Api`, `Api -> …`, `lives in Domain`) | `namespace` | |
| Namespace globs (`Orders.Domain.**`) | `namespace` | |
| Templates (`I{Name}`) | `variable` | |
| Attribute names (`[Route(*)]`, `except [X]`) | `decorator` | |
| Type patterns and `bind`/`ignore` type names | `type` | `defaultLibrary` when the name resolves in the compilation, none when it does not |

Tokens are derived from Core's token stream and AST — never from regular expressions. Comments are
recovered from the gaps between tokens, because the lexer discards them. Multi-line block comments
are split per line, as the protocol requires.

The `defaultLibrary` modifier is the "resolved" signal. With no compilation loaded nothing carries
it, which is correct: the server genuinely does not know.

### Completion

The context comes from the token stream, so it keeps working in a half-typed buffer:

| Position | Offered |
| --- | --- |
| Top level (brace depth 0) | `project`, `contract`, `bind`, `ignore` |
| Inside `project { }` | `root`, `layer`, `allow`, `nothing` plus every declared layer (for `->` lines) |
| Inside a contract, member block or refinement | `require`, `forbid`, `match`, `where`, member kinds, predicate keywords, modifiers, condition keywords |
| After `in` or `layer` | Declared layer names, with their globs as detail |
| After `extends`, `to`, `from` | Declared contract names, with their `match` clause as detail |
| After `inherits`, `implements`, `injects`, `returns`, `new` | C# simple type names, prefix-filtered |
| After `using` | Namespaces from the compilation, prefix-filtered |
| Inside `[ … ]` | Attribute names with the `Attribute` suffix stripped, prefix-filtered |

Symbol-backed lists are capped at **200** items and marked `isIncomplete` when truncated, so the
client re-queries as the user types. Requesting one of them starts the compilation load in the
background; the first request after opening a workspace usually returns an empty list.

Comma- and pipe-separated continuations are handled: in `injects only IMediator, |` the governing
keyword is still `injects`.

### Hover

- **Contract name** — `extends`, the `match` clause, the rule count split into own and inherited,
  and, once a compilation is loaded, the list of currently bound types (capped at 30).
- **Layer name** — its glob, the layers it may depend on, and the always-allowed framework globs.
- **Type pattern** — the resolved symbol's fully qualified signature, the `<summary>` from its XML
  documentation when there is one, and its namespace; or **unresolved**.
- **Template** — what `{Name}`, `{Namespace}` and `{Name:strip Suffix}` expand to.

The bound-type list is computed by evaluating each contract's `match` clause plus any `bind`
statements against every type declared in the workspace's own projects. **Only the cheap half of a
`match` clause is evaluated**: the type kind, the namespace glob, and `name starts with` /
`name ends with` / `name contains` conditions (combined with `and` / `or`). `inherits`,
`implements`, `attributes`, `name matches` and modifier conditions are assumed to hold; when that
happens the hover says *(approximate)* and explains that the list may be wider than the real
binding. See §6.

### Go to definition

- Contract reference or declaration → the `contract <Name>` identifier.
- Layer reference or declaration → the `layer <Name>` identifier.
- Type pattern or `bind`/`ignore` type name → every C# source declaration of the resolved symbol
  (partial types give several), when the compilation is loaded.

### Find references

- On a contract: its declaration, every `extends`, `bind … to` and `ignore … from` mention in the
  `.tan` files, plus every `[Tangent("Name")]` attribute argument in C# source once the compilation
  is loaded. The attribute search is **syntax only** — it walks syntax trees looking for an
  attribute named `Tangent` or `TangentAttribute` with a string literal first argument. Binding
  every attribute in the solution would cost far more than the feature is worth.
- On a layer: every mention in the `.tan` files.

`context.includeDeclaration` is honoured.

---

## 3. C# compilation loading

Loading is lazy, single-flight and entirely optional:

1. The first request that needs it (or `initialized`, in the background) calls `BeginLoad`.
2. A `*.slnx` or `*.sln` is looked for in the directory containing `.tangent` and up to eight
   ancestors. If none is found, every `*.csproj` under that directory (excluding `bin` and `obj`) is
   used instead. If neither exists, loading stops before MSBuild is ever touched.
3. `MSBuildLocator.RegisterDefaults()` runs once per process, guarded by `IsRegistered`.
4. `MSBuildWorkspace.Create()` opens the solution or the projects and a `Compilation` is obtained
   per project.
5. A `TypeIndex` is built: simple name → types, all namespaces, all attribute names. Types declared
   in the workspace's own projects are indexed in full; types from referenced assemblies are indexed
   when public. Source assemblies are collected before references, so a project-referenced type is
   recorded as *source*, not as metadata.

Progress and failures go to `window/logMessage`. `.slnx` solutions work (Roslyn 5.0 parses them);
loading `samples/Orders/Orders.slnx` indexes about 7 900 types.

Set **`TANGENT_LSP_NO_COMPILATION`** to any non-empty value to skip MSBuild entirely — useful in CI,
in tests, or when MSBuild discovery misbehaves.

The compilation is loaded once and reused. Editing `.tan` files never reloads it; editing C# files
is not tracked at all (see §6).

---

## 4. The custom request

```
method: "tangent/contractDefinition"
params: { "contract": string }
result: Location | null
```

Given a contract name it returns the LSP `Location` of the `contract <Name>` **identifier** (not the
whole declaration) in whichever `.tan` file declares it, or `null` when no such contract exists.

The VS Code extension registers a definition provider for C# documents; when the cursor sits inside
the string argument of `[Tangent("Controller")]` it sends this request and jumps to the result. The
server needs no compilation to answer it.

Implemented by `ContractDefinitionHandler`, an `IJsonRpcRequestHandler<ContractDefinitionParams,
ContractDefinitionResponse?>` carrying `[Method("tangent/contractDefinition", Direction.ClientToServer)]`.

---

## 5. Debugging

**From VS Code.** Set `tangent.server.trace` to `verbose` and watch the **Tangent** output channel;
it carries the full JSON-RPC traffic plus the server's own `window/logMessage` lines (`Tangent:
watching …`, `Tangent: loading solution …`, `Tangent: indexed N types …`).

**From a terminal.** `dotnet tangent serve` speaks LSP on stdio; anything you type must be framed
with `Content-Length`. A small harness is easier:

```csharp
var toServer = new System.IO.Pipelines.Pipe();
var fromServer = new System.IO.Pipelines.Pipe();
var server = TangentLanguageServerHost.RunAsync(
    toServer.Reader.AsStream(), fromServer.Writer.AsStream(), root, CancellationToken.None);
// write `Content-Length: N\r\n\r\n{…}` to toServer.Writer.AsStream(), read from fromServer.Reader.AsStream()
```

`tests/Tangent.LanguageServer.Tests/LspTestClient.cs` is exactly this, in about 200 lines, and is
the fastest way to reproduce a wire-level problem.

**Tests.**

```
dotnet test tests/Tangent.LanguageServer.Tests
```

Each test builds a throwaway `.tangent` folder in the temp directory with no solution anywhere near
it, so the whole suite exercises the no-compilation path. Compilation-backed behaviour is not
covered automatically because MSBuild in a test host is slow and environment-dependent; verify it
by hand against `samples/Orders`.

**Common problems.**

| Symptom | Cause |
| --- | --- |
| No diagnostics at all | No `.tangent` folder was found. The log line after `initialized` says so. |
| No C#-backed features | No solution or project near `.tangent`, or `TANGENT_LSP_NO_COMPILATION` is set. Check the log. |
| Type patterns never get `defaultLibrary` | Same as above; the compilation has not loaded. |
| TNG1020 on a type you know exists | The type lives in a project not reachable from the discovered solution. |

---

## 6. Limitations

- **`match` clauses are evaluated approximately.** `inherits`, `implements`, `attributes`,
  `name matches` (regex) and modifier conditions are treated as satisfied when computing the bound
  types shown on hover, which can widen the list. Hover labels such results *(approximate)*. The
  analyzer, not the language server, is the authority on what is actually bound.
- **The compilation is a snapshot.** It is loaded once and never refreshed. Renaming a C# type, or
  adding a project, needs a server restart (`Tangent: Restart Language Server`).
- **Type-pattern resolution is by name, not by binding.** Generic arity and type arguments are
  ignored when deciding whether a name resolves, and an ambiguous simple name resolves to whichever
  type was indexed first. Good enough for colouring and go-to-definition; not a substitute for the
  analyzer.
- **`[Tangent("X")]` references are found by syntax.** An attribute whose argument is a constant
  expression rather than a literal (`[Tangent(Contracts.Controller)]`) is not found.
- **No rename, no formatting, no code actions, no document symbols.** Not in §13 of the plan.
- **No file-system watching of `.tan` files.** The VS Code client watches `**/.tangent/**/*.tan` and
  the server re-reads from disk on every rebuild, so external edits are picked up on the next change
  or save, but a file changed outside the editor with nothing else happening will not republish on
  its own.
- **Diagnostics are push-only.** The pull model (`textDocument/diagnostic`) is not implemented.
- **One workspace folder.** The first folder that contains a `.tangent` directory wins; a
  multi-root workspace with several `.tangent` folders only gets the first.
