using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using Tangent.LanguageServer.Text;
using Tangent.LanguageServer.Workspace;

namespace Tangent.LanguageServer.Handlers;

/// <summary>
/// Full-text document synchronisation for <c>.tan</c> files. The VS Code client also opens C#
/// documents against this server (it needs the custom contract request), so anything that is not
/// a <c>.tan</c> file is ignored here.
/// </summary>
internal sealed class TangentSyncHandler : TextDocumentSyncHandlerBase
{
    private readonly TangentWorkspace _workspace;

    /// <summary>Creates the handler.</summary>
    public TangentSyncHandler(TangentWorkspace workspace) => _workspace = workspace;

    /// <inheritdoc/>
    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri) =>
        new TextDocumentAttributes(uri, "tangent");

    /// <inheritdoc/>
    public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken)
    {
        string? path = LspText.FromUri(request.TextDocument.Uri);
        if (LspText.IsTangentFile(path))
        {
            _workspace.OpenDocument(path!, request.TextDocument.Text ?? string.Empty);
        }

        return Unit.Task;
    }

    /// <inheritdoc/>
    public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken)
    {
        string? path = LspText.FromUri(request.TextDocument.Uri);
        if (!LspText.IsTangentFile(path)) return Unit.Task;

        TextDocumentContentChangeEvent? change = request.ContentChanges?.LastOrDefault();
        if (change is not null) _workspace.ChangeDocument(path!, change.Text ?? string.Empty);
        return Unit.Task;
    }

    /// <inheritdoc/>
    public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken cancellationToken)
    {
        string? path = LspText.FromUri(request.TextDocument.Uri);
        if (LspText.IsTangentFile(path)) _workspace.Invalidate();
        return Unit.Task;
    }

    /// <inheritdoc/>
    public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken cancellationToken)
    {
        string? path = LspText.FromUri(request.TextDocument.Uri);
        if (LspText.IsTangentFile(path)) _workspace.CloseDocument(path!);
        return Unit.Task;
    }

    /// <inheritdoc/>
    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(
        TextSynchronizationCapability capability,
        ClientCapabilities clientCapabilities) =>
        new TextDocumentSyncRegistrationOptions(TextDocumentSyncKind.Full)
        {
            DocumentSelector = TangentDocumentSelector.Instance,
            Save = new BooleanOr<SaveOptions>(new SaveOptions { IncludeText = false }),
        };
}
