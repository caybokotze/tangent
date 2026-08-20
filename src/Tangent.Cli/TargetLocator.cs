using System.Text.RegularExpressions;

namespace Tangent.Cli;

/// <summary>What the <c>path</c> argument resolved to: a solution or a single project, plus the
/// <c>.tangent</c> folder that governs it.</summary>
public sealed class Target
{
    internal Target(string path, bool isSolution, string root, string? tangentFolder, IReadOnlyList<string> contractFiles)
    {
        Path = path;
        IsSolution = isSolution;
        Root = root;
        TangentFolder = tangentFolder;
        ContractFiles = contractFiles;
    }

    /// <summary>The absolute path of the <c>.sln</c>, <c>.slnx</c> or <c>.csproj</c> file.</summary>
    public string Path { get; }

    /// <summary>True when <see cref="Path"/> is a solution rather than a single project.</summary>
    public bool IsSolution { get; }

    /// <summary>The directory every reported path is relative to: the solution's (or project's) directory.</summary>
    public string Root { get; }

    /// <summary>The <c>.tangent</c> folder, or null when there is none.</summary>
    public string? TangentFolder { get; }

    /// <summary>Every <c>*.tan</c> file under <see cref="TangentFolder"/>, ordered by path.</summary>
    public IReadOnlyList<string> ContractFiles { get; }
}

/// <summary>
/// Turns the <c>path</c> argument into a <see cref="Target"/>: a solution file, a project file, or a
/// directory holding exactly one of either. Also finds the <c>.tangent</c> folder by searching upward
/// from the solution directory.
/// </summary>
public static class TargetLocator
{
    /// <summary>The folder Tangent keeps contracts in.</summary>
    public const string FolderName = ".tangent";

    private static readonly Regex SlnProjectLine = new(
        @"^Project\(""\{[^}]*\}""\)\s*=\s*""[^""]*""\s*,\s*""([^""]+)""",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>Resolves a path, or returns null and writes the reason to <paramref name="error"/>.</summary>
    public static Target? Resolve(string? path, out string? error)
    {
        error = null;
        string candidate = string.IsNullOrWhiteSpace(path) ? Environment.CurrentDirectory : path!;
        candidate = Path.GetFullPath(candidate);

        string? file;
        if (File.Exists(candidate))
        {
            string extension = Path.GetExtension(candidate);
            if (!IsSolutionExtension(extension) && !extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                error = $"'{candidate}' is not a .sln, .slnx or .csproj file.";
                return null;
            }

            file = candidate;
        }
        else if (Directory.Exists(candidate))
        {
            file = FindInDirectory(candidate, out error);
            if (file is null) return null;
        }
        else
        {
            error = $"'{candidate}' does not exist.";
            return null;
        }

        bool isSolution = IsSolutionExtension(Path.GetExtension(file));
        string root = Path.GetDirectoryName(file) ?? candidate;
        string? folder = FindTangentFolder(root);
        IReadOnlyList<string> contracts = folder is null ? Array.Empty<string>() : EnumerateContracts(folder);

        return new Target(file, isSolution, root, folder, contracts);
    }

    /// <summary>Every <c>*.tan</c> file under a <c>.tangent</c> folder, ordered by path.</summary>
    public static IReadOnlyList<string> EnumerateContracts(string folder)
    {
        string[] files = Directory.GetFiles(folder, "*.tan", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);
        return files;
    }

    /// <summary>Searches <paramref name="start"/> and every ancestor for a <c>.tangent</c> directory.</summary>
    public static string? FindTangentFolder(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, FolderName);
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        return null;
    }

    /// <summary>The project files a solution lists, resolved to absolute paths.</summary>
    public static IReadOnlyList<string> ProjectsOf(string solutionPath)
    {
        string directory = Path.GetDirectoryName(solutionPath) ?? Environment.CurrentDirectory;
        var projects = new List<string>();

        string text = File.ReadAllText(solutionPath);
        if (Path.GetExtension(solutionPath).Equals(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            foreach (Match match in Regex.Matches(text, @"<Project\s+Path=""([^""]+)""", RegexOptions.CultureInvariant))
            {
                Add(match.Groups[1].Value);
            }
        }
        else
        {
            foreach (Match match in SlnProjectLine.Matches(text))
            {
                Add(match.Groups[1].Value);
            }
        }

        projects.Sort(StringComparer.Ordinal);
        return projects;

        void Add(string relative)
        {
            string normalised = relative.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            if (!normalised.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) return;
            string full = Path.GetFullPath(Path.Combine(directory, normalised));
            if (File.Exists(full)) projects.Add(full);
        }
    }

    private static string? FindInDirectory(string directory, out string? error)
    {
        error = null;

        var solutions = new List<string>();
        solutions.AddRange(Directory.GetFiles(directory, "*.slnx"));
        solutions.AddRange(Directory.GetFiles(directory, "*.sln"));
        if (solutions.Count == 1) return solutions[0];
        if (solutions.Count > 1)
        {
            solutions.Sort(StringComparer.Ordinal);
            error = $"'{directory}' contains {solutions.Count} solutions ({string.Join(", ", solutions.Select(Path.GetFileName))}); name one explicitly.";
            return null;
        }

        string[] projects = Directory.GetFiles(directory, "*.csproj");
        if (projects.Length == 1) return projects[0];
        if (projects.Length > 1)
        {
            Array.Sort(projects, StringComparer.Ordinal);
            error = $"'{directory}' contains {projects.Length} projects ({string.Join(", ", projects.Select(Path.GetFileName))}); name one explicitly.";
            return null;
        }

        error = $"'{directory}' contains no .sln, .slnx or .csproj file.";
        return null;
    }

    private static bool IsSolutionExtension(string extension) =>
        extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase);
}
