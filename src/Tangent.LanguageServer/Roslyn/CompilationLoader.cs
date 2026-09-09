using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace Tangent.LanguageServer.Roslyn;

/// <summary>
/// Loads the workspace's C# solution lazily and at most once. Every feature that uses it degrades
/// to "no compilation" rather than waiting, so a slow or failing MSBuild never blocks a request.
/// </summary>
internal sealed class CompilationLoader : IDisposable
{
    /// <summary>Set this environment variable to any non-empty value to skip MSBuild entirely.</summary>
    public const string DisableVariable = "TANGENT_LSP_NO_COMPILATION";

    private static readonly object LocatorGate = new();
    private static bool _locatorTried;

    private readonly object _gate = new();
    private readonly Action<string> _log;
    private readonly Action _onLoaded;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _loading;
    private CompilationContext? _context;
    private bool _failed;
    private bool _disposed;

    /// <summary>Creates a loader that reports progress through <paramref name="log"/>.</summary>
    /// <param name="log">Receives progress and failure messages.</param>
    /// <param name="onLoaded">Invoked once the load finishes, so diagnostics that need the compilation can be republished.</param>
    public CompilationLoader(Action<string> log, Action onLoaded)
    {
        _log = log;
        _onLoaded = onLoaded;
    }

    /// <summary>The directory searched for a solution or project files.</summary>
    public string? SearchRoot { get; set; }

    /// <summary>The loaded context, or null while loading, when loading failed, or when nothing was found.</summary>
    public CompilationContext? Context => Volatile.Read(ref _context);

    /// <summary>True once a load attempt has finished, successfully or not.</summary>
    public bool IsSettled => Context is not null || Volatile.Read(ref _failed);

    /// <summary>Starts loading in the background if it has not started already. Never throws and never blocks.</summary>
    public void BeginLoad()
    {
        lock (_gate)
        {
            if (_disposed || _loading is not null || SearchRoot is null) return;
            if (Environment.GetEnvironmentVariable(DisableVariable) is { Length: > 0 })
            {
                Volatile.Write(ref _failed, true);
                _loading = Task.CompletedTask;
                return;
            }

            string root = SearchRoot;
            _loading = Task.Run(() => LoadAsync(root, _cancellation.Token));
        }
    }

    private async Task LoadAsync(string root, CancellationToken cancellationToken)
    {
        try
        {
            string? target = FindSolution(root);
            ImmutableArray<string> projects = target is null ? FindProjects(root) : ImmutableArray<string>.Empty;
            if (target is null && projects.IsEmpty)
            {
                _log("Tangent: no solution or project found under " + root + "; C#-backed features are disabled.");
                Volatile.Write(ref _failed, true);
                return;
            }

            var compilations = ImmutableArray.CreateBuilder<Compilation>();
            string source;

            if (!TryRegisterMsBuild())
            {
                _log("Tangent: no MSBuild instance found; C#-backed features are disabled.");
                Volatile.Write(ref _failed, true);
                return;
            }

            using MSBuildWorkspace workspace = CreateWorkspace();
            using IDisposable failures = workspace.RegisterWorkspaceFailedHandler(e =>
            {
                if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                {
                    _log("Tangent: MSBuild: " + e.Diagnostic.Message);
                }
            });

            if (target is not null)
            {
                _log("Tangent: loading solution " + target);
                Solution solution = await workspace.OpenSolutionAsync(target, cancellationToken: cancellationToken).ConfigureAwait(false);
                foreach (Project project in solution.Projects)
                {
                    Compilation? compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
                    if (compilation is not null) compilations.Add(compilation);
                }

                source = target;
            }
            else
            {
                _log("Tangent: loading " + projects.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + " project(s)");
                foreach (string path in projects)
                {
                    Project project = await workspace.OpenProjectAsync(path, cancellationToken: cancellationToken).ConfigureAwait(false);
                    Compilation? compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
                    if (compilation is not null) compilations.Add(compilation);
                }

                source = root;
            }

            if (compilations.Count == 0)
            {
                _log("Tangent: no compilations were produced; C#-backed features are disabled.");
                Volatile.Write(ref _failed, true);
                return;
            }

            ImmutableArray<Compilation> built = compilations.ToImmutable();
            var context = new CompilationContext(built, TypeIndex.Build(built), source);
            Volatile.Write(ref _context, context);
            _log(
                "Tangent: indexed " +
                context.Types.AllTypes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                " types from " + source);
        }
        catch (OperationCanceledException)
        {
            Volatile.Write(ref _failed, true);
        }
#pragma warning disable CA1031 // A broken solution must never take the language server down.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Volatile.Write(ref _failed, true);
            _log("Tangent: failed to load the C# workspace: " + ex.Message);
        }
        finally
        {
            if (Context is not null) _onLoaded();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static MSBuildWorkspace CreateWorkspace() => MSBuildWorkspace.Create();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool TryRegisterMsBuild()
    {
        lock (LocatorGate)
        {
            if (MSBuildLocator.IsRegistered) return true;
            if (_locatorTried) return MSBuildLocator.IsRegistered;
            _locatorTried = true;
            try
            {
                MSBuildLocator.RegisterDefaults();
                return true;
            }
            catch (InvalidOperationException)
            {
                // Already registered by the host process, or no instance is available.
                return MSBuildLocator.IsRegistered;
            }
        }
    }

    /// <summary>The nearest solution file at or above <paramref name="root"/>, or null.</summary>
    public static string? FindSolution(string root)
    {
        var directory = new DirectoryInfo(root);
        for (int depth = 0; directory is not null && depth < 8; depth++, directory = directory.Parent)
        {
            string? found = EnumerateSolutions(directory).FirstOrDefault();
            if (found is not null) return found;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateSolutions(DirectoryInfo directory)
    {
        string[] patterns = { "*.slnx", "*.sln" };
        foreach (string pattern in patterns)
        {
            FileInfo[] files;
            try
            {
                files = directory.GetFiles(pattern, SearchOption.TopDirectoryOnly);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (FileInfo file in files.OrderBy(static f => f.Name, StringComparer.Ordinal))
            {
                yield return file.FullName;
            }
        }
    }

    private static ImmutableArray<string> FindProjects(string root)
    {
        try
        {
            return Directory
                .EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
                .Where(static p => !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                                && !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .OrderBy(static p => p, StringComparer.Ordinal)
                .ToImmutableArray();
        }
        catch (IOException)
        {
            return ImmutableArray<string>.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return ImmutableArray<string>.Empty;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _cancellation.Cancel();
        _cancellation.Dispose();
    }
}
