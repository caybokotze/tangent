using System.Collections.Immutable;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Tangent.LanguageServer.Features;
using Tangent.LanguageServer.Syntax;
using Tangent.LanguageServer.Text;
using Tangent.LanguageServer.Workspace;

namespace Tangent.LanguageServer.Handlers;

/// <summary>
/// Go to definition: contract and layer references resolve inside the <c>.tangent</c> folder,
/// type patterns resolve into C# source once the compilation is available.
/// </summary>
internal sealed class TangentDefinitionHandler : DefinitionHandlerBase
{
    private readonly TangentWorkspace _workspace;

    /// <summary>Creates the handler.</summary>
    public TangentDefinitionHandler(TangentWorkspace workspace) => _workspace = workspace;

    /// <inheritdoc/>
    public override Task<LocationOrLocationLinks?> Handle(DefinitionParams request, CancellationToken cancellationToken)
    {
        string? path = LspText.FromUri(request.TextDocument.Uri);
        if (!LspText.IsTangentFile(path)) return Empty;

        WorkspaceSnapshot snapshot = _workspace.GetSnapshot();
        TanDocument? document = snapshot.Find(path);
        if (document is null) return Empty;

        int offset = document.Lines.OffsetOf(request.Position.Line, request.Position.Character);
        if (document.RegionAt(offset) is not TanRegion region) return Empty;

        if (region.IsContract)
        {
            LspLocation? location = Navigation.ContractDeclaration(snapshot, region.Text);
            return location is null ? Empty : SingleAsync(location);
        }

        if (region.IsLayer)
        {
            LspLocation? location = Navigation.LayerDeclaration(snapshot, region.Text);
            return location is null ? Empty : SingleAsync(location);
        }

        if (region.IsTypeName)
        {
            _workspace.RequestCompilation();
            ImmutableArray<LspLocation> locations = Navigation.TypeDeclarations(_workspace.Compilation, region.Text);
            if (locations.IsEmpty) return Empty;
            return Task.FromResult<LocationOrLocationLinks?>(new LocationOrLocationLinks(locations.Select(static l => new LocationOrLocationLink(l))));
        }

        return Empty;
    }

    private static Task<LocationOrLocationLinks?> Empty =>
        Task.FromResult<LocationOrLocationLinks?>(new LocationOrLocationLinks());

    private static Task<LocationOrLocationLinks?> SingleAsync(LspLocation location) =>
        Task.FromResult<LocationOrLocationLinks?>(new LocationOrLocationLinks(new LocationOrLocationLink(location)));

    /// <inheritdoc/>
    protected override DefinitionRegistrationOptions CreateRegistrationOptions(
        DefinitionCapability capability,
        ClientCapabilities clientCapabilities) =>
        new DefinitionRegistrationOptions { DocumentSelector = TangentDocumentSelector.Instance };
}
