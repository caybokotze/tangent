using System.Collections.Immutable;
using System.Text;
using Tangent.Core.Binding;
using Tangent.Core.Diagnostics;
using Tangent.Core.Syntax;
using Tangent.Core.Text;

namespace Tangent.Core.Tests;

/// <summary>One golden case: a single <c>.tan</c> file, or a directory of them bound as one compilation unit.</summary>
public sealed class GoldenCase
{
    public GoldenCase(string name, string directory, ImmutableArray<string> files)
    {
        Name = name;
        Directory = directory;
        Files = files;
    }

    public string Name { get; }

    public string Directory { get; }

    public ImmutableArray<string> Files { get; }

    public string GoldenPath(string suffix) => Path.Combine(Directory, Name + "." + suffix);
}

/// <summary>Locates and loads the golden cases that live next to this file.</summary>
public static class GoldenCases
{
    /// <summary>Set <c>TANGENT_UPDATE_GOLDENS=1</c> to rewrite every golden instead of comparing.</summary>
    public static bool UpdateMode =>
        Environment.GetEnvironmentVariable("TANGENT_UPDATE_GOLDENS") is string value &&
        (value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));

    /// <summary>The <c>cases</c> directory in the test project source tree.</summary>
    public static string Root { get; } = Locate();

    // Not [CallerFilePath]: a CI build maps source paths to "/_/", so the compiled path does not exist.
    private static string Locate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Tangent.slnx")))
            {
                return Path.Combine(directory.FullName, "tests", "Tangent.Core.Tests", "cases");
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Tangent.slnx was not found above " + AppContext.BaseDirectory + ".");
    }

    /// <summary>Every case name, for <c>[MemberData]</c>.</summary>
    public static IEnumerable<object[]> Names()
    {
        foreach (GoldenCase testCase in All())
        {
            yield return new object[] { testCase.Name };
        }
    }

    /// <summary>Every case, single-file cases first then multi-file directories, both in name order.</summary>
    public static IEnumerable<GoldenCase> All()
    {
        var files = System.IO.Directory.GetFiles(Root, "*.tan", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.Ordinal);
        foreach (string file in files)
        {
            yield return new GoldenCase(
                Path.GetFileNameWithoutExtension(file),
                Root,
                ImmutableArray.Create(file));
        }

        var directories = System.IO.Directory.GetDirectories(Root);
        Array.Sort(directories, StringComparer.Ordinal);
        foreach (string directory in directories)
        {
            var members = System.IO.Directory.GetFiles(directory, "*.tan", SearchOption.TopDirectoryOnly);
            if (members.Length == 0) continue;
            Array.Sort(members, StringComparer.Ordinal);
            yield return new GoldenCase(
                Path.GetFileName(directory),
                directory,
                members.ToImmutableArray());
        }
    }

    /// <summary>Loads one case by name.</summary>
    public static GoldenCase Load(string name)
    {
        foreach (GoldenCase testCase in All())
        {
            if (string.Equals(testCase.Name, name, StringComparison.Ordinal)) return testCase;
        }

        throw new InvalidOperationException("No golden case named '" + name + "' under " + Root);
    }

    /// <summary>Reads the case's files as <see cref="SourceFile"/>s, using bare file names so goldens stay portable.</summary>
    public static SourceFile[] ReadSources(GoldenCase testCase)
    {
        var sources = new SourceFile[testCase.Files.Length];
        for (int i = 0; i < testCase.Files.Length; i++)
        {
            string text = File.ReadAllText(testCase.Files[i]).Replace("\r\n", "\n");
            sources[i] = new SourceFile(Path.GetFileName(testCase.Files[i]), text);
        }

        return sources;
    }

    /// <summary>Renders every token stream, one section per file.</summary>
    public static string RenderTokens(ImmutableArray<SyntaxTree> trees)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < trees.Length; i++)
        {
            sb.Append("-- ").Append(trees[i].Source.Path).Append(" --\n");
            sb.Append(SyntaxTreePrinter.PrintTokens(trees[i]));
        }

        return sb.ToString();
    }

    /// <summary>Renders every AST, one section per file.</summary>
    public static string RenderAst(ImmutableArray<SyntaxTree> trees)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < trees.Length; i++)
        {
            sb.Append("-- ").Append(trees[i].Source.Path).Append(" --\n");
            sb.Append(SyntaxTreePrinter.PrintAst(trees[i]));
        }

        return sb.ToString();
    }

    /// <summary>Renders diagnostics one per line, or a marker when there are none.</summary>
    public static string RenderDiagnostics(ImmutableArray<TangentDiagnostic> diagnostics)
    {
        if (diagnostics.Length == 0) return "(no diagnostics)\n";
        var sb = new StringBuilder();
        for (int i = 0; i < diagnostics.Length; i++)
        {
            sb.Append(diagnostics[i].ToString()).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>Compares text against a golden file, or rewrites it in update mode.</summary>
    public static void Verify(GoldenCase testCase, string suffix, string actual)
    {
        string path = testCase.GoldenPath(suffix);
        actual = actual.Replace("\r\n", "\n");

        if (UpdateMode)
        {
            File.WriteAllText(path, actual, new UTF8Encoding(false));
            return;
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                "Missing golden '" + path + "'. Run the tests with TANGENT_UPDATE_GOLDENS=1 to create it.");
        }

        string expected = File.ReadAllText(path).Replace("\r\n", "\n");
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new GoldenMismatchException(path, expected, actual);
        }
    }

    /// <summary>Parses and binds a case.</summary>
    public static (ImmutableArray<SyntaxTree> Trees, BindResult Result) Run(GoldenCase testCase)
    {
        SourceFile[] sources = ReadSources(testCase);
        ImmutableArray<SyntaxTree> trees = ModelLoader.ParseAll(sources);
        BindResult result = Binder.Bind(trees);
        return (trees, result);
    }
}

/// <summary>Thrown when a golden file does not match, with a readable diff.</summary>
public sealed class GoldenMismatchException : Exception
{
    public GoldenMismatchException(string path, string expected, string actual)
        : base(BuildMessage(path, expected, actual))
    {
    }

    private static string BuildMessage(string path, string expected, string actual)
    {
        string[] expectedLines = expected.Split('\n');
        string[] actualLines = actual.Split('\n');
        var sb = new StringBuilder();
        sb.Append("Golden mismatch: ").Append(path).Append('\n');
        sb.Append("Re-run with TANGENT_UPDATE_GOLDENS=1 to accept the new output.\n");

        int max = Math.Max(expectedLines.Length, actualLines.Length);
        int shown = 0;
        for (int i = 0; i < max && shown < 12; i++)
        {
            string e = i < expectedLines.Length ? expectedLines[i] : "<missing>";
            string a = i < actualLines.Length ? actualLines[i] : "<missing>";
            if (string.Equals(e, a, StringComparison.Ordinal)) continue;
            sb.Append("  line ").Append(i + 1).Append('\n');
            sb.Append("    expected: ").Append(e).Append('\n');
            sb.Append("    actual:   ").Append(a).Append('\n');
            shown++;
        }

        return sb.ToString();
    }
}
