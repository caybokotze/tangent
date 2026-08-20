namespace Tangent.Cli;

/// <summary>
/// The two writers a command prints through, plus the colour policy. Colour is plain ANSI — Tangent
/// takes no console-formatting dependency — and is off whenever <c>NO_COLOR</c> is set, <c>--no-color</c>
/// was passed, or stdout is redirected.
/// </summary>
public sealed class CliConsole
{
    /// <summary>Creates a console.</summary>
    public CliConsole(TextWriter output, TextWriter error, bool color, bool verbose)
    {
        Out = output;
        Error = error;
        Color = color;
        Verbose = verbose;
    }

    /// <summary>Standard output.</summary>
    public TextWriter Out { get; }

    /// <summary>Standard error.</summary>
    public TextWriter Error { get; }

    /// <summary>True when ANSI escapes may be emitted.</summary>
    public bool Color { get; }

    /// <summary>True when <c>--verbose</c> was passed.</summary>
    public bool Verbose { get; }

    /// <summary>Decides whether colour is allowed for this process.</summary>
    public static bool ShouldUseColor(bool noColorRequested)
    {
        if (noColorRequested) return false;
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"))) return false;
        try
        {
            return !Console.IsOutputRedirected;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>Wraps <paramref name="text"/> in an ANSI colour when colour is enabled.</summary>
    public string Paint(string text, AnsiColor color) =>
        Color ? "\u001b[" + (int)color + "m" + text + "\u001b[0m" : text;

    /// <summary>Writes a line to standard output.</summary>
    public void WriteLine(string text = "") => Out.WriteLine(text);

    /// <summary>Writes a line to standard error.</summary>
    public void WriteError(string text) => Error.WriteLine(text);

    /// <summary>Writes a line to standard error only when <c>--verbose</c> was passed.</summary>
    public void Trace(string text)
    {
        if (Verbose) Error.WriteLine(text);
    }
}

/// <summary>The handful of ANSI SGR colours the CLI uses.</summary>
public enum AnsiColor
{
    /// <summary>Dim grey, for paths and other chrome.</summary>
    Grey = 90,

    /// <summary>Red, for errors.</summary>
    Red = 31,

    /// <summary>Yellow, for warnings.</summary>
    Yellow = 33,

    /// <summary>Green, for a clean result.</summary>
    Green = 32,

    /// <summary>Cyan, for headings.</summary>
    Cyan = 36,

    /// <summary>Bold, for group headings.</summary>
    Bold = 1,
}
