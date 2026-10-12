# Tangent for VS Code

Editor support for [Tangent](../../README.md), an architectural conformance checker for .NET
whose contracts are written in `.tan` files.

## Features

- **Syntax highlighting** for `.tan` files via the `source.tangent` TextMate grammar: comments,
  strings, keywords, C# modifiers, member kinds, predicates, layer and contract names, type patterns
  with generics and `*` wildcards, attribute literals (`[Route(*)]`), namespace globs
  (`Orders.Domain.**`) and templates (`{Name}`, `{Namespace}`, `{Name:strip Suffix}`).
- **Language configuration**: `//` and `/* */` comments, bracket matching and auto-closing pairs,
  brace folding.
- **Language server** (`tangent serve`, started over stdio): diagnostics, semantic tokens,
  completion, hover, go-to-definition and find-references for `.tan` files.
- **Go to definition from C#**: put the cursor inside the string argument of `[Tangent("Controller")]`
  and press <kbd>F12</kbd> to jump to the `contract Controller { ... }` declaration.
- **Commands**: `Tangent: Restart Language Server`, `Tangent: Show Dependency Graph`.

The grammar is regex-only and knows nothing about declared names; it is the fallback for users
without the language server.

## Requirements

The language server ships with the Tangent CLI:

```
dotnet tool install -g Tangent.Cli
```

If it is missing, the extension shows a warning once per session with the install command and keeps
syntax highlighting working.

## Settings

| Setting | Default | Description |
| --- | --- | --- |
| `tangent.server.command` | `dotnet` | Executable used to launch the language server. |
| `tangent.server.args` | `["tangent", "serve"]` | Arguments passed to that executable. |
| `tangent.server.trace` | `off` | LSP trace level (`off`, `messages`, `verbose`); written to the **Tangent** output channel. |
| `tangent.viewerUrl` | `http://localhost:5173` | URL opened by `Tangent: Show Dependency Graph` (the `tangent.graph.json` viewer). |

The extension makes no network calls; the only process it starts is the local language server.

## Custom LSP request

For the C#-side go-to-definition the extension sends a single custom request:

```
method: "tangent/contractDefinition"
params: { "contract": string }
result: Location | null          // LSP Location: { uri, range: { start, end } }
```

## Running from source

```
cd editors/vscode
npm install
npm run compile
```

Then press <kbd>F5</kbd> ("Run Extension") to launch an Extension Development Host with
`test-fixtures/` opened as the workspace. `npm run watch` keeps the build incremental.

## Packaging

```
npm run package        # vsce package --no-dependencies
```

This produces `tangent-vscode-<version>.vsix`, installable with
`code --install-extension tangent-vscode-<version>.vsix`.

## Known limitations

- The C# definition provider inspects a single line, so a `[Tangent("...")]` attribute split across
  lines is not recognised.
- Everything beyond highlighting requires the language server to be installed and running.
