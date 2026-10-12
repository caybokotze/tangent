using Tangent.Graph;

namespace Tangent.Cli.Output;

/// <summary>
/// The human-readable report: findings grouped by contract (with "Layers", "Model" and "Binding" for the
/// diagnostics that belong to no contract), then by file, and a one-line summary.
/// </summary>
public static class ConsoleReporter
{
    /// <summary>Prints the report and returns the summary counts.</summary>
    /// <param name="console">Where to print.</param>
    /// <param name="findings">The findings to report, exemptions excluded.</param>
    /// <param name="exemptions">TNG9000 notes; listed only when <paramref name="showExemptions"/> is true.</param>
    /// <param name="root">The directory paths are printed relative to.</param>
    /// <param name="projectCount">How many projects were analysed.</param>
    /// <param name="showExemptions">Whether to list the exemptions that were applied.</param>
    /// <param name="explainer">When not null, each finding is followed by its rule and the offending line.</param>
    public static void Report(
        CliConsole console,
        IReadOnlyList<Finding> findings,
        IReadOnlyList<Finding> exemptions,
        string root,
        int projectCount,
        bool showExemptions,
        Explainer? explainer = null)
    {
        foreach (IGrouping<string, Finding> group in findings
                     .GroupBy(f => f.Group, StringComparer.Ordinal)
                     .OrderBy(g => GroupRank(g.Key))
                     .ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            console.WriteLine(console.Paint(group.Key, AnsiColor.Cyan));

            foreach (Finding finding in group
                         .OrderBy(f => GraphBuilder.Relative(root, f.File), StringComparer.Ordinal)
                         .ThenBy(f => f.Line)
                         .ThenBy(f => f.Col)
                         .ThenBy(f => f.Code, StringComparer.Ordinal))
            {
                console.WriteLine(Line(console, finding, root));
                if (explainer?.Explain(finding) is { } explanation) WriteExplanation(console, finding, explanation);
            }

            console.WriteLine();
        }

        if (showExemptions && exemptions.Count > 0)
        {
            console.WriteLine(console.Paint("Exemptions", AnsiColor.Cyan));
            foreach (Finding exemption in exemptions)
            {
                console.WriteLine(Line(console, exemption, root));
            }

            console.WriteLine();
        }

        console.WriteLine(Summary(console, findings, exemptions.Count, projectCount));
    }

    /// <summary>Renders one finding as <c>  {severity} {code} {file}:{line}:{col}  {message}</c>.</summary>
    public static string Line(CliConsole console, Finding finding, string root)
    {
        string location = finding.File.Length == 0
            ? "<no location>"
            : GraphBuilder.Relative(root, finding.File) + ":" + finding.Line + ":" + finding.Col;

        return "  " +
               console.Paint(finding.Severity, ColorOf(finding.Severity)) + " " +
               console.Paint(finding.Code, AnsiColor.Bold) + " " +
               console.Paint(location, AnsiColor.Grey) + "  " +
               finding.Message;
    }

    /// <summary>
    /// Writes the <c>--explain</c> block under a finding: the rule, where it is declared, what applies it,
    /// and the offending line with a caret under the reported span.
    /// </summary>
    public static void WriteExplanation(CliConsole console, Finding finding, Explanation explanation)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(finding);
        ArgumentNullException.ThrowIfNull(explanation);

        console.WriteLine(Label(console, "rule") + explanation.Rule);
        if (explanation.Declared is not null) console.WriteLine(Label(console, "declared") + explanation.Declared);
        if (explanation.AppliesTo is not null) console.WriteLine(Label(console, "applies to") + explanation.AppliesTo);
        if (explanation.Source is null) return;

        string number = finding.Line.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string gutter = new(' ', number.Length);
        console.WriteLine(ExplainIndent + console.Paint(number + " | ", AnsiColor.Grey) + explanation.Source);

        int start = Math.Max(0, finding.Col - 1 - explanation.SourceIndent);
        if (start >= explanation.Source.Length) return;
        int end = finding.EndLine == finding.Line
            ? Math.Min(explanation.Source.Length, finding.EndCol - 1 - explanation.SourceIndent)
            : explanation.Source.Length;
        int length = Math.Max(1, end - start);

        console.WriteLine(ExplainIndent + console.Paint(gutter + " | ", AnsiColor.Grey) +
                          new string(' ', start) + console.Paint(new string('^', length), AnsiColor.Red));
    }

    private const string ExplainIndent = "      ";

    private static string Label(CliConsole console, string label) =>
        ExplainIndent + console.Paint(label.PadRight(12), AnsiColor.Grey);

    /// <summary>Renders the trailing summary line.</summary>
    public static string Summary(CliConsole console, IReadOnlyList<Finding> findings, int exemptionCount, int projectCount)
    {
        int errors = 0;
        int warnings = 0;
        foreach (Finding finding in findings)
        {
            if (finding.Severity == "error") errors++;
            else if (finding.Severity == "warning") warnings++;
        }

        if (findings.Count == 0)
        {
            return console.Paint("0 violations", AnsiColor.Green) +
                   $" in {projectCount} {Plural(projectCount, "project")}, {exemptionCount} {Plural(exemptionCount, "exemption")} applied";
        }

        string head = $"{findings.Count} {Plural(findings.Count, "violation")} ({errors} {Plural(errors, "error")}, {warnings} {Plural(warnings, "warning")})";
        return console.Paint(head, errors > 0 ? AnsiColor.Red : AnsiColor.Yellow) +
               $" in {projectCount} {Plural(projectCount, "project")}, {exemptionCount} {Plural(exemptionCount, "exemption")} applied";
    }

    private static string Plural(int count, string word) => count == 1 ? word : word + "s";

    private static AnsiColor ColorOf(string severity) => severity switch
    {
        "error" => AnsiColor.Red,
        "warning" => AnsiColor.Yellow,
        _ => AnsiColor.Grey,
    };

    // Model and binding problems come first: they explain everything else.
    private static int GroupRank(string group) => group switch
    {
        "Model" => 0,
        "Binding" => 1,
        "Layers" => 3,
        _ => 2,
    };
}
