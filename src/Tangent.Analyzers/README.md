# Tangent.Analyzers

The Roslyn analyzer for [Tangent](https://github.com/caybokotze/tangent), an architectural conformance checker for .NET.

You describe the architecture of a solution in `.tan` files in a `.tangent/` folder at the solution root: the layers, which way dependencies may flow, and the rules for controllers, handlers, repositories and entities. This package reports every place the code breaks those rules, in the IDE and in `dotnet build`.

## Install

```bash
dotnet add package Tangent.Analyzers
```

The package is a development dependency, so it never flows to the projects that reference yours. To check every project, add the reference once in `Directory.Build.props` at the solution root:

```xml
<ItemGroup>
  <PackageReference Include="Tangent.Analyzers" Version="x.y.z" PrivateAssets="all" />
</ItemGroup>
```

The package finds the nearest folder above each project that contains `.tangent/`, and gives every `.tan` file in it to the compiler. Set the `TangentRoot` property to use another folder.

## A contract

```tangent
project "Orders" {
    layer Api            = Orders.Api.**
    layer Application    = Orders.Application.**
    layer Domain         = Orders.Domain.**
    layer Infrastructure = Orders.Infrastructure.**

    Api            -> Application, Domain
    Application    -> Domain
    Infrastructure -> Application, Domain
    Domain         -> nothing
}

contract Controller {
    match: class in Api where name ends with "Controller"

    sealed
    attributes [ApiController], [Route(*)]
    ctor { injects only IMediator, ILogger<*> }
    forbid using Orders.Infrastructure.**
}
```

## What you get

- **Diagnostics** with ids `TNG0001` to `TNG9000`. Each message names the broken rule and the `.tan` line that declares it.
- **Code fixes** for the mechanical rules, such as a missing `sealed` or a missing attribute.
- **A `Tangent.Contracts` class** with one constant per contract, so `[Tangent(Contracts.Controller)]` binds a type to a contract without a string that can be misspelt.

Violations are warnings by default. To fail the build on any of them, add this to `.editorconfig`:

```ini
[*.cs]
dotnet_analyzer_diagnostic.category-Tangent.severity = error
```

## Related packages

- [`Tangent.Cli`](https://www.nuget.org/packages/Tangent.Cli): the `tangent` command for CI, SARIF reports, fixes and the architecture graph.
- [`Tangent.Attributes`](https://www.nuget.org/packages/Tangent.Attributes): `[Tangent]` and `[TangentIgnore]`.

The [language reference](https://github.com/caybokotze/tangent/blob/main/docs/getting-started.md) and the [diagnostic catalogue](https://github.com/caybokotze/tangent/blob/main/docs/diagnostics.md) are on GitHub.
