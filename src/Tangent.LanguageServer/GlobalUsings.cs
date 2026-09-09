// Aliases used across the server. `Range` collides with `System.Range` and `Location`,
// `Diagnostic`, `TypeKind` and `SyntaxTree` all exist in both Roslyn and the LSP protocol,
// so every ambiguous name is given one unambiguous spelling here.
global using CoreSyntaxTree = Tangent.Core.Syntax.SyntaxTree;
global using LspLocation = OmniSharp.Extensions.LanguageServer.Protocol.Models.Location;
global using LspPosition = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;
global using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
