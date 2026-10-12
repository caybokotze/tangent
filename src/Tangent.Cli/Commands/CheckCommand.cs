using Tangent.Cli.Output;
using Tangent.Graph;

namespace Tangent.Cli.Commands;

/// <summary>The options <c>tangent check</c> accepts.</summary>
public sealed class CheckOptions
{
    /// <summary>The solution, project or directory to check.</summary>
    public string? Path { get; init; }

    /// <summary>Where to write a SARIF 2.1.0 log, or null.</summary>
    public string? Sarif { get; init; }

    /// <summary>Where to write the JSON report, or null.</summary>
    public string? Json { get; init; }

    /// <summary>The lowest severity that makes the command fail. Defaults to <c>error</c>.</summary>
    public string FailOn { get; init; } = "error";

    /// <summary>List the TNG9000 notes for the ignored violations.</summary>
    public bool ShowIgnored { get; init; }

    /// <summary>Restrict the run to these project names.</summary>
    public IReadOnlyList<string> Projects { get; init; } = Array.Empty<string>();

    /// <summary>Treat warnings as errors.</summary>
    public bool Strict { get; init; }

    /// <summary>Follow each finding with the rule text, its declaration and the offending line.</summary>
    public bool Explain { get; init; }
}

/// <summary><c>tangent check</c>: the command CI runs.</summary>
public static class CheckCommand
{
    /// <summary>Runs the command.</summary>
    public static async Task<int> RunAsync(CheckOptions options, CliConsole console, CancellationToken cancellationToken)
    {
        Target? target = TargetLocator.Resolve(options.Path, out string? error);
        if (target is null)
        {
            console.WriteError("error: " + error);
            return ExitCodes.LoadFailure;
        }

        using AnalysisResult analysis = await AnalysisRunner
            .RunAsync(target, options.Projects, exporter: null, includeUnbound: false, console, cancellationToken)
            .ConfigureAwait(false);

        return Report(analysis, options, console);
    }

    /// <summary>
    /// Prints the report and writes whatever files were asked for. Shared with <c>tangent graph</c> and
    /// <c>tangent diff</c>, which reuse the same single analysis run.
    /// </summary>
    internal static int Report(AnalysisResult analysis, CheckOptions options, CliConsole console)
    {
        if (analysis.CompilerErrors > 0)
        {
            console.WriteError(console.Paint(
                $"warning: {analysis.CompilerErrors} C# compiler {(analysis.CompilerErrors == 1 ? "error" : "errors")} — " +
                "analysis of a broken compilation is unreliable. Run with --verbose to see them.",
                AnsiColor.Yellow));
        }

        var reported = new List<Finding>();
        var ignored = new List<Finding>();

        foreach (Finding finding in analysis.Findings)
        {
            if (finding.IsIgnored) { ignored.Add(finding); continue; }
            if (finding.Severity == "hidden") continue;
            if (options.Strict) finding.Promote();
            reported.Add(finding);
        }

        string root = analysis.Target.Root;
        Explainer? explainer = options.Explain && analysis.Model.Model is { } model ? new Explainer(model, root) : null;
        ConsoleReporter.Report(console, reported, ignored, root, analysis.Projects.Count, options.ShowIgnored, explainer);

        var written = new List<Finding>(reported);
        if (options.ShowIgnored) written.AddRange(ignored);

        if (options.Json is not null)
        {
            Write(options.Json, FindingsJsonWriter.Write(written, root, explainer), console, "JSON report");
        }

        if (options.Sarif is not null)
        {
            Write(options.Sarif, SarifWriter.Write(written, root, explainer), console, "SARIF log");
        }

        return ExitCodeFor(reported, options.FailOn);
    }

    /// <summary>Decides the exit code: model errors beat rule violations, which beat a clean run.</summary>
    internal static int ExitCodeFor(IReadOnlyList<Finding> findings, string failOn)
    {
        bool modelError = false;
        bool failing = false;
        bool warningFails = string.Equals(failOn, "warning", StringComparison.OrdinalIgnoreCase);

        foreach (Finding finding in findings)
        {
            if (finding.IsModelDiagnostic && finding.Severity == "error") modelError = true;
            if (finding.Severity == "error" || (warningFails && finding.Severity == "warning")) failing = true;
        }

        if (modelError) return ExitCodes.ModelError;
        return failing ? ExitCodes.Violations : ExitCodes.Clean;
    }

    private static void Write(string path, string content, CliConsole console, string what)
    {
        string full = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(full, content);
        console.WriteLine($"Wrote {what} to {GraphBuilder.Relative(Environment.CurrentDirectory, full)}");
    }
}
