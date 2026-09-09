using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Tangent.LanguageServer.Features;
using Tangent.LanguageServer.Text;
using Tangent.LanguageServer.Workspace;

namespace Tangent.LanguageServer.Handlers;

/// <summary>Context-aware completion for <c>.tan</c> files.</summary>
internal sealed class TangentCompletionHandler : CompletionHandlerBase
{
    private readonly TangentWorkspace _workspace;

    /// <summary>Creates the handler.</summary>
    public TangentCompletionHandler(TangentWorkspace workspace) => _workspace = workspace;

    /// <inheritdoc/>
    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
    {
        string? path = LspText.FromUri(request.TextDocument.Uri);
        if (!LspText.IsTangentFile(path)) return Task.FromResult(new CompletionList());

        WorkspaceSnapshot snapshot = _workspace.GetSnapshot();
        TanDocument? document = snapshot.Find(path);
        if (document is null) return Task.FromResult(new CompletionList());

        int offset = document.Lines.OffsetOf(request.Position.Line, request.Position.Character);
        TanCompletionContext context = CompletionContextAnalyzer.Analyze(document, offset);

        if (context.Kind is CompletionContextKind.TypeNames
            or CompletionContextKind.Namespaces
            or CompletionContextKind.Attributes)
        {
            _workspace.RequestCompilation();
        }

        return Task.FromResult(CompletionEngine.Build(context, snapshot, _workspace.Compilation));
    }

    /// <inheritdoc/>
    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken cancellationToken) =>
        Task.FromResult(request);

    /// <inheritdoc/>
    protected override CompletionRegistrationOptions CreateRegistrationOptions(
        CompletionCapability capability,
        ClientCapabilities clientCapabilities) =>
        new CompletionRegistrationOptions
        {
            DocumentSelector = TangentDocumentSelector.Instance,
            ResolveProvider = false,
            TriggerCharacters = new Container<string>(" ", "[", ".", ","),
        };
}
