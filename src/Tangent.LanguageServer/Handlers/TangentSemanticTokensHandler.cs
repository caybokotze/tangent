using System.Collections.Immutable;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Tangent.LanguageServer.Roslyn;
using Tangent.LanguageServer.Syntax;
using Tangent.LanguageServer.Text;
using Tangent.LanguageServer.Workspace;

namespace Tangent.LanguageServer.Handlers;

/// <summary>
/// Semantic tokens for <c>.tan</c> files, derived from the Core token stream and AST.
/// Resolved C# type names get the <c>defaultLibrary</c> modifier; unresolved ones get none,
/// so an editor theme can grey out names the compilation does not know.
/// </summary>
internal sealed class TangentSemanticTokensHandler : SemanticTokensHandlerBase
{
    private readonly TangentWorkspace _workspace;

    /// <summary>Creates the handler.</summary>
    public TangentSemanticTokensHandler(TangentWorkspace workspace) => _workspace = workspace;

    /// <summary>The legend this server publishes; tests assert token ids against it.</summary>
    public static SemanticTokensLegend Legend { get; } = new SemanticTokensLegend
    {
        TokenTypes = new Container<SemanticTokenType>(
            SemanticTokenType.Keyword,
            SemanticTokenType.Type,
            SemanticTokenType.Namespace,
            SemanticTokenType.Class,
            SemanticTokenType.Variable,
            SemanticTokenType.String,
            SemanticTokenType.Comment,
            SemanticTokenType.Operator,
            SemanticTokenType.Decorator),
        TokenModifiers = new Container<SemanticTokenModifier>(
            SemanticTokenModifier.Declaration,
            SemanticTokenModifier.DefaultLibrary),
    };

    /// <inheritdoc/>
    protected override SemanticTokensRegistrationOptions CreateRegistrationOptions(
        SemanticTokensCapability capability,
        ClientCapabilities clientCapabilities) =>
        new SemanticTokensRegistrationOptions
        {
            DocumentSelector = TangentDocumentSelector.Instance,
            Legend = Legend,
            Full = new BooleanOr<SemanticTokensCapabilityRequestFull>(new SemanticTokensCapabilityRequestFull { Delta = false }),
            Range = new BooleanOr<SemanticTokensCapabilityRequestRange>(true),
        };

    /// <inheritdoc/>
    protected override Task<SemanticTokensDocument> GetSemanticTokensDocument(
        ITextDocumentIdentifierParams @params,
        CancellationToken cancellationToken) =>
        Task.FromResult(new SemanticTokensDocument(Legend));

    /// <inheritdoc/>
    protected override Task Tokenize(
        SemanticTokensBuilder builder,
        ITextDocumentIdentifierParams identifier,
        CancellationToken cancellationToken)
    {
        string? path = LspText.FromUri(identifier.TextDocument.Uri);
        if (!LspText.IsTangentFile(path)) return Task.CompletedTask;

        WorkspaceSnapshot snapshot = _workspace.GetSnapshot();
        TanDocument? document = snapshot.Find(path);
        if (document is null) return Task.CompletedTask;

        CompilationContext? compilation = _workspace.Compilation;
        foreach (TanRegion region in document.Regions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (SemanticTokenType type, SemanticTokenModifier[] modifiers) = Map(region, compilation);
            PushSplit(builder, document, region, type, modifiers);
        }

        return Task.CompletedTask;
    }

    /// <summary>Pushes a region, splitting it across lines because a semantic token may not span lines.</summary>
    private static void PushSplit(
        SemanticTokensBuilder builder,
        TanDocument document,
        TanRegion region,
        SemanticTokenType type,
        SemanticTokenModifier[] modifiers)
    {
        int start = region.Start;
        int end = region.End;
        while (start < end)
        {
            int line = document.Lines.LineOf(start);
            int lineEnd = document.Lines.LineEnd(line);
            int stop = end < lineEnd ? end : lineEnd;
            if (stop > start)
            {
                (int _, int character) = document.Lines.PositionOf(start);
                builder.Push(line, character, stop - start, type, modifiers);
            }

            int nextLineStart = document.Lines.LineStart(line + 1);
            start = nextLineStart > start ? nextLineStart : end;
        }
    }

    private static readonly SemanticTokenModifier[] None = Array.Empty<SemanticTokenModifier>();
    private static readonly SemanticTokenModifier[] Declaration = { SemanticTokenModifier.Declaration };
    private static readonly SemanticTokenModifier[] DefaultLibrary = { SemanticTokenModifier.DefaultLibrary };

    private static (SemanticTokenType Type, SemanticTokenModifier[] Modifiers) Map(
        TanRegion region,
        CompilationContext? compilation) => region.Kind switch
        {
            TanRegionKind.Keyword => (SemanticTokenType.Keyword, None),
            TanRegionKind.Operator => (SemanticTokenType.Operator, None),
            TanRegionKind.StringLiteral => (SemanticTokenType.String, None),
            TanRegionKind.Comment => (SemanticTokenType.Comment, None),
            TanRegionKind.ContractDeclaration => (SemanticTokenType.Class, Declaration),
            TanRegionKind.ContractReference => (SemanticTokenType.Class, None),
            TanRegionKind.LayerDeclaration => (SemanticTokenType.Namespace, Declaration),
            TanRegionKind.LayerReference => (SemanticTokenType.Namespace, None),
            TanRegionKind.Glob => (SemanticTokenType.Namespace, None),
            TanRegionKind.TypeTemplate => (SemanticTokenType.Variable, None),
            TanRegionKind.AttributeName => (SemanticTokenType.Decorator, None),
            _ => (SemanticTokenType.Type, Resolves(region.Text, compilation) ? DefaultLibrary : None),
        };

    private static bool Resolves(string name, CompilationContext? compilation) =>
        compilation is not null && compilation.Types.TryResolve(name, out Microsoft.CodeAnalysis.INamedTypeSymbol _);

    /// <summary>The legend index of a token type, for tests.</summary>
    public static int TokenTypeIndex(SemanticTokenType type)
    {
        ImmutableArray<SemanticTokenType> types = Legend.TokenTypes.ToImmutableArray();
        for (int i = 0; i < types.Length; i++)
        {
            if (types[i].Equals(type)) return i;
        }

        return -1;
    }
}
