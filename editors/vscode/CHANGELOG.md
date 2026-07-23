# Changelog

All notable changes to the Tangent VS Code extension are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.1.0] - Unreleased

### Added

- `tangent` language for `.tan` files with a language configuration (line and block comments,
  brackets, auto-closing pairs, brace folding).
- `source.tangent` TextMate grammar covering comments, strings, keywords, C# modifiers, member
  kinds, predicates, layer and contract names, type patterns with generics and wildcards, attribute
  literals, namespace globs and templates.
- Language client for `tangent serve` over stdio, configurable through `tangent.server.command`,
  `tangent.server.args` and `tangent.server.trace`.
- C# definition provider that resolves `[Tangent("Contract")]` to the contract declaration via the
  custom `tangent/contractDefinition` request.
- Commands `tangent.restartServer` and `tangent.showGraph`, the latter opening `tangent.viewerUrl`.
- Reference `.tan` fixtures under `test-fixtures/` for grammar checking.
