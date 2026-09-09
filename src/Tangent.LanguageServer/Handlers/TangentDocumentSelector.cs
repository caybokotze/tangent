using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Tangent.LanguageServer.Handlers;

/// <summary>The document selector every <c>.tan</c> handler registers for.</summary>
internal static class TangentDocumentSelector
{
    /// <summary>Matches documents with the <c>tangent</c> language id or a <c>.tan</c> extension.</summary>
    public static TextDocumentSelector Instance { get; } = new TextDocumentSelector(
        TextDocumentFilter.ForLanguage("tangent"),
        TextDocumentFilter.ForPattern("**/*.tan"));
}
