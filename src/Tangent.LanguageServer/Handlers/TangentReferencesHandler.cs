using System.Collections.Immutable;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Tangent.LanguageServer.Features;
using Tangent.LanguageServer.Syntax;
using Tangent.LanguageServer.Text;
using Tangent.LanguageServer.Workspace;

namespace Tangent.LanguageServer.Handlers;

/// <summary>Find references for contract and layer names.</summary>
internal sealed class TangentReferencesHandler : ReferencesHandlerBase
{
    private readonly TangentWorkspace _workspace;

    /// <summary>Creates the handler.</summary>
    public TangentReferencesHandler(TangentWorkspace workspace) => _workspace = workspace;

    /// <inheritdoc/>
    public override Task<LocationContainer?> Handle(ReferenceParams request, CancellationToken cancellationToken)
    {
        string? path = LspText.FromUri(request.TextDocument.Uri);
        if (!LspText.IsTangentFile(path)) return Empty;

        WorkspaceSnapshot snapshot = _workspace.GetSnapshot();
        TanDocument? document = snapshot.Find(path);
        if (document is null) return Empty;

        int offset = document.Lines.OffsetOf(request.Position.Line, request.Position.Character);
        if (document.RegionAt(offset) is not TanRegion region) return Empty;

        bool includeDeclaration = request.Context?.IncludeDeclaration ?? true;

        if (region.IsContract)
        {
            _workspace.RequestCompilation();
            ImmutableArray<LspLocation> locations =
                Navigation.ContractReferences(snapshot, region.Text, _workspace.Compilation, includeDeclaration);
            return Task.FromResult<LocationContainer?>(new LocationContainer(locations));
        }

        if (region.IsLayer)
        {
            return Task.FromResult<LocationContainer?>(new LocationContainer(
                Navigation.LayerReferences(snapshot, region.Text, includeDeclaration)));
        }

        return Empty;
    }

    private static Task<LocationContainer?> Empty => Task.FromResult<LocationContainer?>(new LocationContainer());

    /// <inheritdoc/>
    protected override ReferenceRegistrationOptions CreateRegistrationOptions(
        ReferenceCapability capability,
        ClientCapabilities clientCapabilities) =>
        new ReferenceRegistrationOptions { DocumentSelector = TangentDocumentSelector.Instance };
}
