using System.Reflection;

namespace Tangent.Cli.Serve;

/// <summary>
/// The built viewer that <c>tangent graph --serve</c> serves. The build embeds <c>viewer/dist</c> when it
/// exists (see <c>Tangent.Cli.csproj</c>), so a file is addressed by its path inside that folder and never
/// by a path on disk.
/// </summary>
internal sealed class ViewerAssets
{
    private const string ResourcePrefix = "viewer/";

    private readonly Dictionary<string, ViewerFile> _files;

    internal ViewerAssets(IEnumerable<KeyValuePair<string, byte[]>> files)
    {
        _files = new Dictionary<string, ViewerFile>(StringComparer.Ordinal);
        foreach ((string path, byte[] content) in files)
        {
            _files[path] = new ViewerFile(ContentTypeOf(path), content);
        }
    }

    /// <summary>True when the viewer is present: a build without <c>viewer/dist</c> embeds nothing.</summary>
    internal bool HasViewer => _files.ContainsKey("index.html");

    /// <summary>The paths of the files, inside <c>viewer/dist</c>.</summary>
    internal IEnumerable<string> Paths => _files.Keys;

    /// <summary>Reads the viewer that this assembly embeds.</summary>
    internal static ViewerAssets FromAssembly()
    {
        Assembly assembly = typeof(ViewerAssets).Assembly;
        var files = new List<KeyValuePair<string, byte[]>>();
        foreach (string name in assembly.GetManifestResourceNames())
        {
            // MSBuild writes %(RecursiveDir) with the separator of the build machine.
            string path = name.Replace('\\', '/');
            if (!path.StartsWith(ResourcePrefix, StringComparison.Ordinal)) continue;

            using Stream stream = assembly.GetManifestResourceStream(name)!;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            files.Add(new KeyValuePair<string, byte[]>(path[ResourcePrefix.Length..], buffer.ToArray()));
        }

        return new ViewerAssets(files);
    }

    /// <summary>Finds a file by its path inside <c>viewer/dist</c>, for example <c>assets/index.js</c>.</summary>
    internal bool TryGet(string path, out ViewerFile file) => _files.TryGetValue(path, out file!);

    private static string ContentTypeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".woff2" => "font/woff2",
        ".wasm" => "application/wasm",
        _ => "application/octet-stream",
    };
}

/// <summary>One file of the viewer, with the content type it is served with.</summary>
internal sealed record ViewerFile(string ContentType, byte[] Content);
