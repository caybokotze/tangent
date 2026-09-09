using System.Collections.Immutable;
using Tangent.Core.Binding;
using Tangent.Core.Diagnostics;
using Tangent.Core.Ir;
using Tangent.Core.Syntax;
using Tangent.LanguageServer.Syntax;
using Tangent.LanguageServer.Text;

namespace Tangent.LanguageServer.Workspace;

/// <summary>One <c>.tan</c> file as the server currently sees it: text, parse tree and classification.</summary>
internal sealed class TanDocument
{
    /// <summary>Creates a document.</summary>
    public TanDocument(string path, string text, SyntaxTree tree)
    {
        Path = path;
        Text = text;
        Lines = new LineIndex(text);
        Tree = tree;
        Regions = TanClassifier.Classify(tree);
    }

    /// <summary>The normalized absolute path.</summary>
    public string Path { get; }

    /// <summary>The full text.</summary>
    public string Text { get; }

    /// <summary>The line index for position conversion.</summary>
    public LineIndex Lines { get; }

    /// <summary>The parse tree.</summary>
    public SyntaxTree Tree { get; }

    /// <summary>Position-sorted classified regions.</summary>
    public ImmutableArray<TanRegion> Regions { get; }

    /// <summary>The region at a character offset, or null.</summary>
    public TanRegion? RegionAt(int offset) => TanClassifier.FindAt(Regions, offset);
}

/// <summary>An immutable view of the whole <c>.tangent</c> folder: every file, parsed and bound together.</summary>
internal sealed class WorkspaceSnapshot
{
    private readonly Dictionary<string, int> _indexByPath;

    /// <summary>Creates a snapshot.</summary>
    public WorkspaceSnapshot(
        string? folder,
        ImmutableArray<TanDocument> documents,
        BindResult bind,
        ImmutableArray<TangentDiagnostic> extraDiagnostics)
    {
        Folder = folder;
        Documents = documents;
        Bind = bind;
        ExtraDiagnostics = extraDiagnostics;
        _indexByPath = new Dictionary<string, int>(documents.Length, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < documents.Length; i++) _indexByPath[documents[i].Path] = i;
    }

    /// <summary>An empty snapshot for a workspace with no <c>.tangent</c> folder.</summary>
    public static WorkspaceSnapshot Empty { get; } = new WorkspaceSnapshot(
        null,
        ImmutableArray<TanDocument>.Empty,
        new BindResult(null, ImmutableArray<TangentDiagnostic>.Empty),
        ImmutableArray<TangentDiagnostic>.Empty);

    /// <summary>The <c>.tangent</c> folder this snapshot covers, or null.</summary>
    public string? Folder { get; }

    /// <summary>Every <c>.tan</c> file, in binder file-index order.</summary>
    public ImmutableArray<TanDocument> Documents { get; }

    /// <summary>The bind result, including parse diagnostics.</summary>
    public BindResult Bind { get; }

    /// <summary>Diagnostics the server adds on top of Core's, such as TNG1020.</summary>
    public ImmutableArray<TangentDiagnostic> ExtraDiagnostics { get; }

    /// <summary>The bound model, or null when nothing could be produced.</summary>
    public ArchitectureModel? Model => Bind.Model;

    /// <summary>The document for a path, or null.</summary>
    public TanDocument? Find(string? path)
    {
        if (path is null) return null;
        return _indexByPath.TryGetValue(path, out int index) ? Documents[index] : null;
    }

    /// <summary>The document a Core source span points into, or null.</summary>
    public TanDocument? DocumentOf(Tangent.Core.Text.SourceSpan span) =>
        span.FileIndex >= 0 && span.FileIndex < Documents.Length ? Documents[span.FileIndex] : null;
}
