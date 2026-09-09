using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Tangent.LanguageServer.Features;
using Tangent.LanguageServer.Syntax;
using Tangent.LanguageServer.Text;
using Tangent.LanguageServer.Workspace;

namespace Tangent.LanguageServer.Handlers;

/// <summary>Hover for contract names, layer names, type patterns and templates.</summary>
internal sealed class TangentHoverHandler : HoverHandlerBase
{
    private readonly TangentWorkspace _workspace;

    /// <summary>Creates the handler.</summary>
    public TangentHoverHandler(TangentWorkspace workspace) => _workspace = workspace;

    /// <inheritdoc/>
    public override Task<Hover?> Handle(HoverParams request, CancellationToken cancellationToken)
    {
        string? path = LspText.FromUri(request.TextDocument.Uri);
        if (!LspText.IsTangentFile(path)) return Task.FromResult<Hover?>(null);

        WorkspaceSnapshot snapshot = _workspace.GetSnapshot();
        TanDocument? document = snapshot.Find(path);
        if (document is null) return Task.FromResult<Hover?>(null);

        int offset = document.Lines.OffsetOf(request.Position.Line, request.Position.Character);
        if (document.RegionAt(offset) is not TanRegion region) return Task.FromResult<Hover?>(null);

        if (region.IsContract || region.IsTypeName) _workspace.RequestCompilation();

        string? content = HoverBuilder.Build(snapshot, region, _workspace.Compilation);
        if (content is null) return Task.FromResult<Hover?>(null);

        return Task.FromResult<Hover?>(new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = content,
            }),
            Range = LspText.ToRange(document.Lines, region.Start, region.Length),
        });
    }

    /// <inheritdoc/>
    protected override HoverRegistrationOptions CreateRegistrationOptions(
        HoverCapability capability,
        ClientCapabilities clientCapabilities) =>
        new HoverRegistrationOptions { DocumentSelector = TangentDocumentSelector.Instance };
}
