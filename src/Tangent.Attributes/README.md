# Tangent.Attributes

The attributes for [Tangent](https://github.com/caybokotze/tangent), an architectural conformance checker for .NET.

- `[Tangent("Controller")]` binds a type to a contract, when the contract's `match` does not select it.
- `[TangentIgnore("reason")]` ignores the violations of a type or a member. The reason is required, and `tangent check --show-ignored` lists every ignore. Set `Contract = "..."` or `Rule = "..."` to ignore one contract or one rule only.

```csharp
[TangentIgnore("calendar feed returns a file, not a view model", Contract = "Controller")]
public async Task<FileContentResult> ExportIcsAsync(DateOnly day) { ... }
```

## Install

```xml
<PackageReference Include="Tangent.Attributes" Version="x.y.z" PrivateAssets="all" ExcludeAssets="runtime" />
```

Both attributes are `[Conditional("TANGENT_ATTRIBUTES")]`. The compiler and the Tangent analyzer see them, but they are not written to your assemblies unless you define `TANGENT_ATTRIBUTES`. With `ExcludeAssets="runtime"`, no Tangent DLL reaches `bin/` or `deps.json` either.

The checks themselves come from [`Tangent.Analyzers`](https://www.nuget.org/packages/Tangent.Analyzers) and [`Tangent.Cli`](https://www.nuget.org/packages/Tangent.Cli).
