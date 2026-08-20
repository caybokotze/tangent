using System.CommandLine;
using Tangent.Cli.Commands;

namespace Tangent.Cli;

/// <summary>
/// <c>dotnet tangent</c>. <see cref="Main"/> is a thin wrapper over <see cref="RunAsync"/>, which takes
/// its writers as arguments so the end-to-end tests can drive every command in-process and read what it
/// printed.
/// </summary>
public static class Program
{
    /// <summary>The process entry point.</summary>
    public static Task<int> Main(string[] args) => RunAsync(args, Console.Out, Console.Error);

    /// <summary>Parses <paramref name="args"/> and runs the command, returning its exit code.</summary>
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken = default)
    {
        RootCommand root = Build(stdout, stderr);

        var configuration = new InvocationConfiguration
        {
            Output = stdout,
            Error = stderr,
            EnableDefaultExceptionHandler = false,
        };

        try
        {
            return await root.Parse(args).InvokeAsync(configuration, cancellationToken).ConfigureAwait(false);
        }
        catch (AnalysisLoadException exception)
        {
            return Fail(stderr, exception.Message);
        }
        catch (OperationCanceledException)
        {
            return Fail(stderr, "cancelled.");
        }
        catch (Exception exception)
        {
            return Fail(stderr, exception.Message);
        }
    }

    private static int Fail(TextWriter stderr, string message)
    {
        stderr.WriteLine("error: " + message);
        return ExitCodes.LoadFailure;
    }

    private static RootCommand Build(TextWriter stdout, TextWriter stderr)
    {
        var noColor = new Option<bool>("--no-color")
        {
            Description = "Never emit ANSI colour. NO_COLOR and a redirected stdout do the same.",
            Recursive = true,
        };

        var verbose = new Option<bool>("--verbose")
        {
            Description = "Write MSBuild and fix diagnostics to standard error.",
            Recursive = true,
        };

        var root = new RootCommand("Architectural conformance for .NET. Contracts live in .tangent/*.tan.");
        root.Add(noColor);
        root.Add(verbose);

        // ---- init ----------------------------------------------------------
        var initForce = new Option<bool>("--force") { Description = "Overwrite an existing .tangent folder." };
        var init = new Command("init", "Scaffold a .tangent folder from the solution's projects.");
        init.Add(NewPathArgument());
        init.Add(initForce);
        init.SetAction(parse => InitCommand.Run(
            parse.GetValue<string?>("path"),
            parse.GetValue(initForce),
            MakeConsole(parse, stdout, stderr, noColor, verbose)));

        // ---- lint ----------------------------------------------------------
        var lintStrict = new Option<bool>("--strict") { Description = "Treat warnings, including TNG1020, as errors." };
        var dumpIr = new Option<bool>("--dump-ir") { Description = "Print the bound model as JSON." };
        var dumpAst = new Option<bool>("--dump-ast") { Description = "Print the tokens and AST of every .tan file." };
        var lint = new Command("lint", "Parse and bind the .tan files; resolve the type names they mention.");
        lint.Add(NewPathArgument());
        lint.Add(lintStrict);
        lint.Add(dumpIr);
        lint.Add(dumpAst);
        lint.SetAction((parse, token) => LintCommand.RunAsync(
            parse.GetValue<string?>("path"),
            parse.GetValue(lintStrict),
            parse.GetValue(dumpIr),
            parse.GetValue(dumpAst),
            MakeConsole(parse, stdout, stderr, noColor, verbose),
            token));

        // ---- check ---------------------------------------------------------
        var sarif = new Option<string?>("--sarif") { Description = "Write a SARIF 2.1.0 log here.", HelpName = "out.sarif" };
        var json = new Option<string?>("--json") { Description = "Write the JSON report here.", HelpName = "out.json" };
        var failOn = new Option<string>("--fail-on")
        {
            Description = "The lowest severity that fails the command. Default: error, so warnings alone still exit 0.",
            DefaultValueFactory = _ => "error",
        };
        failOn.AcceptOnlyFromAmong("warning", "error");

        var showExemptions = new Option<bool>("--show-exemptions") { Description = "List the exemptions that suppressed a violation (TNG9000)." };
        var checkProjects = NewProjectOption();
        var checkStrict = new Option<bool>("--strict") { Description = "Treat warnings as errors." };

        var check = new Command("check", "Run every contract over the solution and report what breaks.");
        check.Add(NewPathArgument());
        check.Add(sarif);
        check.Add(json);
        check.Add(failOn);
        check.Add(showExemptions);
        check.Add(checkProjects);
        check.Add(checkStrict);
        check.SetAction((parse, token) => CheckCommand.RunAsync(
            new CheckOptions
            {
                Path = parse.GetValue<string?>("path"),
                Sarif = parse.GetValue(sarif),
                Json = parse.GetValue(json),
                FailOn = parse.GetValue(failOn) ?? "error",
                ShowExemptions = parse.GetValue(showExemptions),
                Projects = parse.GetValue(checkProjects) ?? [],
                Strict = parse.GetValue(checkStrict),
            },
            MakeConsole(parse, stdout, stderr, noColor, verbose),
            token));

        // ---- diff ----------------------------------------------------------
        var applyOption = new Option<bool>("--apply") { Description = "Write the fixes to disk instead of only printing them." };
        var diffProjects = NewProjectOption();
        var diff = new Command("diff", "Show the mechanical fixes as a unified diff.");
        diff.Add(NewPathArgument());
        diff.Add(applyOption);
        diff.Add(diffProjects);
        diff.SetAction((parse, token) => DiffCommand.RunAsync(
            parse.GetValue<string?>("path"),
            parse.GetValue(applyOption),
            parse.GetValue(diffProjects) ?? [],
            MakeConsole(parse, stdout, stderr, noColor, verbose),
            token));

        // ---- graph ---------------------------------------------------------
        var output = new Option<string?>("--output", "-o") { Description = "Where to write the graph. Default: <solution>/tangent.graph.json.", HelpName = "file" };
        var includeUnbound = new Option<bool>("--include-unbound") { Description = "Add types that no contract binds, so the graph shows the whole solution." };
        var graphProjects = NewProjectOption();
        var graph = new Command("graph", "Export tangent.graph.json for the viewer.");
        graph.Add(NewPathArgument());
        graph.Add(output);
        graph.Add(includeUnbound);
        graph.Add(graphProjects);
        graph.SetAction((parse, token) => GraphCommand.RunAsync(
            parse.GetValue<string?>("path"),
            parse.GetValue(output),
            parse.GetValue(includeUnbound),
            parse.GetValue(graphProjects) ?? [],
            MakeConsole(parse, stdout, stderr, noColor, verbose),
            token));

        // ---- serve ---------------------------------------------------------
        var workspaceOption = new Option<string?>("--workspace") { Description = "The folder the server treats as the workspace root.", HelpName = "path" };
        var serve = new Command("serve", "Run the .tan language server on stdio.");
        serve.Add(workspaceOption);
        serve.SetAction((parse, token) => ServeCommand.RunAsync(parse.GetValue(workspaceOption), stderr, token));

        root.Add(init);
        root.Add(lint);
        root.Add(check);
        root.Add(diff);
        root.Add(graph);
        root.Add(serve);
        return root;
    }

    // Every subcommand needs its own Argument instance: a symbol may not be shared between commands.
    private static Argument<string?> NewPathArgument() =>
        new("path")
        {
            Description = "A .sln, .slnx or .csproj file, or a directory holding exactly one of them. Defaults to the current directory.",
            Arity = ArgumentArity.ZeroOrOne,
        };

    private static Option<string[]> NewProjectOption() =>
        new("--project") { Description = "Only this project. Repeat the option for several.", HelpName = "name" };

    private static CliConsole MakeConsole(
        System.CommandLine.ParseResult parse,
        TextWriter stdout,
        TextWriter stderr,
        Option<bool> noColor,
        Option<bool> verbose) =>
        new(stdout, stderr, CliConsole.ShouldUseColor(parse.GetValue(noColor)), parse.GetValue(verbose));
}
