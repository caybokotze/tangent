using System.Collections.Immutable;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using Tangent.Core.Binding;
using Tangent.Core.Diagnostics;
using Tangent.Core.Syntax;
using Tangent.Core.Text;
using Tangent.LanguageServer.Roslyn;
using Tangent.LanguageServer.Text;
using CoreSeverity = Tangent.Core.Diagnostics.DiagnosticSeverity;
using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using LspSeverity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity;

namespace Tangent.LanguageServer.Workspace;

/// <summary>
/// The server's model of one <c>.tangent</c> folder. Disk contents are overridden by open editor
/// buffers; every change re-parses the changed file and re-binds the whole folder, which is cheap
/// and keeps cross-file diagnostics correct even for files nobody has open.
/// </summary>
internal sealed class TangentWorkspace : IDisposable
{
    private const int DebounceMilliseconds = 150;

    private readonly object _gate = new();
    private readonly Dictionary<string, string> _openDocuments = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<DocumentUri> _published = new();
    private readonly Timer _debounce;
    private readonly CompilationLoader _compilations;

    private string? _folder;
    private string? _workspaceRoot;
    private WorkspaceSnapshot _snapshot = WorkspaceSnapshot.Empty;
    private bool _dirty = true;
    private ILanguageServerFacade? _server;
    private bool _disposed;

    /// <summary>Creates a workspace rooted at <paramref name="workspaceRoot"/>, which may be null.</summary>
    public TangentWorkspace(string? workspaceRoot)
    {
        _compilations = new CompilationLoader(Log, Invalidate);
        _debounce = new Timer(static state => ((TangentWorkspace)state!).OnDebounce(), this, Timeout.Infinite, Timeout.Infinite);
        if (workspaceRoot is not null) SetWorkspaceRoot(workspaceRoot);
    }

    /// <summary>The discovered <c>.tangent</c> folder, or null when none was found yet.</summary>
    public string? Folder
    {
        get { lock (_gate) { return _folder; } }
    }

    /// <summary>The lazily loaded C# compilations; null until (and unless) they load.</summary>
    public CompilationContext? Compilation => _compilations.Context;

    /// <summary>Requests the C# workspace in the background. Safe to call from any request handler.</summary>
    public void RequestCompilation() => _compilations.BeginLoad();

    /// <summary>Connects the workspace to the running server so it can publish diagnostics and log.</summary>
    public void Attach(ILanguageServerFacade server)
    {
        lock (_gate) { _server = server; }
    }

    /// <summary>Points the workspace at a root directory, discovering the <c>.tangent</c> folder under it.</summary>
    public void SetWorkspaceRoot(string? root)
    {
        if (string.IsNullOrEmpty(root)) return;
        string normalized = LspText.NormalizePath(root);
        lock (_gate)
        {
            _workspaceRoot = normalized;
            if (_folder is null)
            {
                _folder = DiscoverFolder(normalized);
                _dirty = true;
            }

            _compilations.SearchRoot = _folder is not null ? Path.GetDirectoryName(_folder) ?? normalized : normalized;
        }
    }

    /// <summary>Records that a <c>.tan</c> file was opened, adopting its folder when none is known.</summary>
    public void OpenDocument(string path, string text)
    {
        lock (_gate)
        {
            _openDocuments[path] = text;
            if (_folder is null)
            {
                _folder = FolderOf(path);
                if (_folder is not null)
                {
                    _compilations.SearchRoot = _workspaceRoot ?? Path.GetDirectoryName(_folder);
                }
            }

            _dirty = true;
        }

        ScheduleRefresh();
    }

    /// <summary>Replaces the contents of an open document.</summary>
    public void ChangeDocument(string path, string text)
    {
        lock (_gate)
        {
            _openDocuments[path] = text;
            _dirty = true;
        }

        ScheduleRefresh();
    }

    /// <summary>Drops an editor buffer, falling back to the file on disk.</summary>
    public void CloseDocument(string path)
    {
        lock (_gate)
        {
            _openDocuments.Remove(path);
            _dirty = true;
        }

        ScheduleRefresh();
    }

    /// <summary>Marks the model stale, for example after a file system watcher event.</summary>
    public void Invalidate()
    {
        lock (_gate) { _dirty = true; }
        ScheduleRefresh();
    }

    /// <summary>The current snapshot, rebuilding synchronously when a change is still pending.</summary>
    public WorkspaceSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            if (!_dirty) return _snapshot;
            _snapshot = Build();
            _dirty = false;
            return _snapshot;
        }
    }

    /// <summary>Rebuilds if needed and publishes diagnostics for every file in the folder.</summary>
    public void RefreshAndPublish()
    {
        WorkspaceSnapshot snapshot = GetSnapshot();
        PublishDiagnostics(snapshot);
    }

    private void ScheduleRefresh()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _debounce.Change(DebounceMilliseconds, Timeout.Infinite);
        }
    }

    private void OnDebounce()
    {
        try
        {
            RefreshAndPublish();
        }
#pragma warning disable CA1031 // A background refresh must never crash the server.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Log("Tangent: refresh failed: " + ex.Message);
        }
    }

    // ------------------------------------------------------------------
    // building
    // ------------------------------------------------------------------

    private WorkspaceSnapshot Build()
    {
        string? folder = _folder;
        if (folder is null) return WorkspaceSnapshot.Empty;

        ImmutableArray<string> paths = FilesIn(folder);
        var sources = new SourceFile[paths.Length];
        for (int i = 0; i < paths.Length; i++)
        {
            sources[i] = new SourceFile(paths[i], ReadText(paths[i]));
        }

        ImmutableArray<SyntaxTree> trees = ModelLoaderParseAll(sources);
        BindResult bind = Binder.Bind(trees);

        var documents = ImmutableArray.CreateBuilder<TanDocument>(trees.Length);
        for (int i = 0; i < trees.Length; i++)
        {
            documents.Add(new TanDocument(sources[i].Path, sources[i].Text, trees[i]));
        }

        ImmutableArray<TanDocument> built = documents.ToImmutable();
        ImmutableArray<TangentDiagnostic> extra = UnresolvedTypeAnalyzer.Analyze(built, _compilations.Context);
        return new WorkspaceSnapshot(folder, built, bind, extra);
    }

    private static ImmutableArray<SyntaxTree> ModelLoaderParseAll(SourceFile[] sources) =>
        Core.ModelLoader.ParseAll(sources);

    private string ReadText(string path)
    {
        if (_openDocuments.TryGetValue(path, out string? text)) return text;
        try
        {
            return File.ReadAllText(path);
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private ImmutableArray<string> FilesIn(string folder)
    {
        var paths = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (string file in Directory.EnumerateFiles(folder, "*.tan", SearchOption.AllDirectories))
            {
                paths.Add(LspText.NormalizePath(file));
            }
        }
        catch (IOException)
        {
            // Fall through: open buffers below still give us something to work with.
        }
        catch (UnauthorizedAccessException)
        {
            // Same.
        }

        foreach (string open in _openDocuments.Keys)
        {
            if (IsUnder(folder, open)) paths.Add(open);
        }

        return paths.ToImmutableArray();
    }

    private static bool IsUnder(string folder, string path) =>
        path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(folder + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------------
    // folder discovery
    // ------------------------------------------------------------------

    /// <summary>The nearest ancestor directory named <c>.tangent</c>, or null.</summary>
    public static string? FolderOf(string tanFilePath)
    {
        DirectoryInfo? directory = new FileInfo(tanFilePath).Directory;
        while (directory is not null)
        {
            if (string.Equals(directory.Name, ".tangent", StringComparison.OrdinalIgnoreCase))
            {
                return LspText.NormalizePath(directory.FullName);
            }

            directory = directory.Parent;
        }

        return null;
    }

    /// <summary>The <c>.tangent</c> folder at or below <paramref name="root"/>, shallowest first, or null.</summary>
    public static string? DiscoverFolder(string root)
    {
        string direct = Path.Combine(root, ".tangent");
        if (Directory.Exists(direct)) return LspText.NormalizePath(direct);

        var queue = new Queue<string>();
        queue.Enqueue(root);
        int visited = 0;
        while (queue.Count > 0 && visited < 2000)
        {
            string current = queue.Dequeue();
            visited++;
            string[] children;
            try
            {
                children = Directory.GetDirectories(current);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string child in children)
            {
                string name = Path.GetFileName(child);
                if (string.Equals(name, ".tangent", StringComparison.OrdinalIgnoreCase))
                {
                    return LspText.NormalizePath(child);
                }

                if (name.Length > 0 && (name[0] == '.' || IsIgnoredDirectory(name))) continue;
                queue.Enqueue(child);
            }
        }

        return null;
    }

    private static bool IsIgnoredDirectory(string name) =>
        string.Equals(name, "bin", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "obj", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "node_modules", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "artifacts", StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------------
    // diagnostics
    // ------------------------------------------------------------------

    private void PublishDiagnostics(WorkspaceSnapshot snapshot)
    {
        ILanguageServerFacade? server;
        lock (_gate) { server = _server; }
        if (server is null) return;

        var byPath = new Dictionary<string, List<LspDiagnostic>>(StringComparer.OrdinalIgnoreCase);
        foreach (TanDocument document in snapshot.Documents)
        {
            byPath[document.Path] = new List<LspDiagnostic>();
        }

        foreach (TangentDiagnostic diagnostic in snapshot.Bind.Diagnostics)
        {
            AddDiagnostic(snapshot, byPath, diagnostic);
        }

        foreach (TangentDiagnostic diagnostic in snapshot.ExtraDiagnostics)
        {
            AddDiagnostic(snapshot, byPath, diagnostic);
        }

        var current = new HashSet<DocumentUri>();
        foreach (KeyValuePair<string, List<LspDiagnostic>> pair in byPath)
        {
            DocumentUri uri = LspText.ToUri(pair.Key);
            current.Add(uri);
            server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
            {
                Uri = uri,
                Diagnostics = new Container<LspDiagnostic>(pair.Value),
            });
        }

        lock (_gate)
        {
            foreach (DocumentUri stale in _published)
            {
                if (current.Contains(stale)) continue;
                server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
                {
                    Uri = stale,
                    Diagnostics = new Container<LspDiagnostic>(),
                });
            }

            _published.Clear();
            foreach (DocumentUri uri in current) _published.Add(uri);
        }
    }

    private static void AddDiagnostic(
        WorkspaceSnapshot snapshot,
        Dictionary<string, List<LspDiagnostic>> byPath,
        TangentDiagnostic diagnostic)
    {
        if (diagnostic.Severity == CoreSeverity.Hidden) return;

        TanDocument? document = snapshot.DocumentOf(diagnostic.Span) ?? snapshot.Find(diagnostic.FilePath);
        if (document is null) return;
        if (!byPath.TryGetValue(document.Path, out List<LspDiagnostic>? list)) return;

        list.Add(new LspDiagnostic
        {
            Code = diagnostic.Code,
            Severity = ToLspSeverity(diagnostic.Severity),
            Message = diagnostic.Message,
            Source = "tangent",
            Range = LspText.ToRange(document.Lines, diagnostic.Span),
        });
    }

    private static LspSeverity ToLspSeverity(CoreSeverity severity) => severity switch
    {
        CoreSeverity.Error => LspSeverity.Error,
        CoreSeverity.Warning => LspSeverity.Warning,
        CoreSeverity.Info => LspSeverity.Information,
        _ => LspSeverity.Hint,
    };

    private void Log(string message)
    {
        ILanguageServerFacade? server;
        lock (_gate) { server = _server; }
        server?.Window.Log(message);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _debounce.Dispose();
        _compilations.Dispose();
    }
}
