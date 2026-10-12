using Tangent.Cli;
using Xunit;

namespace Tangent.Cli.Tests;

/// <summary>The result of one in-process CLI invocation.</summary>
/// <param name="ExitCode">What <see cref="Program.RunAsync"/> returned.</param>
/// <param name="Out">Everything written to standard output.</param>
/// <param name="Error">Everything written to standard error.</param>
public sealed record CliRun(int ExitCode, string Out, string Error);

/// <summary>
/// Runs the CLI in-process. Loading an MSBuild workspace costs seconds, so tests that need a full
/// analysis share one through <see cref="OrdersFixture"/> rather than paying for it each time.
/// </summary>
public static class Cli
{
    /// <summary>Invokes the CLI with <paramref name="args"/> and captures both streams.</summary>
    public static CliRun Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        // There is no synchronization context in a test host, so there is nothing to deadlock against,
        // and blocking here keeps every test and the collection fixture's constructor synchronous.
#pragma warning disable VSTHRD002
        int exit = Program.RunAsync(args, stdout, stderr).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002

        return new CliRun(exit, stdout.ToString(), stderr.ToString());
    }

    /// <summary>The repository root: the directory that holds <c>Tangent.slnx</c>.</summary>
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary><c>samples/Orders</c>.</summary>
    public static string OrdersSample { get; } = Path.Combine(RepositoryRoot, "samples", "Orders");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Tangent.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Tangent.slnx was not found above " + AppContext.BaseDirectory + ".");
    }
}

/// <summary>
/// One analysis of <c>samples/Orders</c>, shared by every test that needs it: a <c>check</c> with both
/// report files, a bound and an unbound <c>graph</c>, a <c>diff</c>, and a <c>check --explain</c>.
/// </summary>
public sealed class OrdersFixture : IDisposable
{
    /// <summary>Loads the sample and runs every command once.</summary>
    public OrdersFixture()
    {
        Directory.CreateDirectory(TempDirectory);

        JsonPath = Path.Combine(TempDirectory, "check.json");
        SarifPath = Path.Combine(TempDirectory, "check.sarif");
        GraphPath = Path.Combine(TempDirectory, "tangent.graph.json");
        UnboundGraphPath = Path.Combine(TempDirectory, "unbound.graph.json");
        ExplainJsonPath = Path.Combine(TempDirectory, "explain.json");
        ExplainSarifPath = Path.Combine(TempDirectory, "explain.sarif");

        Check = Cli.Run("check", Cli.OrdersSample, "--json", JsonPath, "--sarif", SarifPath, "--no-color");
        Graph = Cli.Run("graph", Cli.OrdersSample, "-o", GraphPath, "--no-color");
        UnboundGraph = Cli.Run("graph", Cli.OrdersSample, "--include-unbound", "--include-source", "-o", UnboundGraphPath, "--no-color");
        Diff = Cli.Run("diff", Cli.OrdersSample, "--no-color");
        ExplainCheck = Cli.Run("check", Cli.OrdersSample, "--explain", "--json", ExplainJsonPath, "--sarif", ExplainSarifPath, "--no-color");

        Json = File.ReadAllText(JsonPath);
        Sarif = File.ReadAllText(SarifPath);
        GraphJson = File.ReadAllText(GraphPath);
        UnboundGraphJson = File.ReadAllText(UnboundGraphPath);
        ExplainJson = File.ReadAllText(ExplainJsonPath);
        ExplainSarif = File.ReadAllText(ExplainSarifPath);
    }

    /// <summary>Where the report files were written.</summary>
    public string TempDirectory { get; } = Path.Combine(
        Path.GetTempPath(), "tangent-cli-tests", Guid.NewGuid().ToString("n"));

    /// <summary>The path of the JSON report.</summary>
    public string JsonPath { get; }

    /// <summary>The path of the SARIF log.</summary>
    public string SarifPath { get; }

    /// <summary>The path of the bound graph.</summary>
    public string GraphPath { get; }

    /// <summary>The path of the graph exported with <c>--include-unbound --include-source</c>.</summary>
    public string UnboundGraphPath { get; }

    /// <summary>The path of the JSON report written with <c>--explain</c>.</summary>
    public string ExplainJsonPath { get; }

    /// <summary>The path of the SARIF log written with <c>--explain</c>.</summary>
    public string ExplainSarifPath { get; }

    /// <summary>The <c>check</c> run.</summary>
    public CliRun Check { get; }

    /// <summary>The <c>graph</c> run.</summary>
    public CliRun Graph { get; }

    /// <summary>The <c>graph --include-unbound --include-source</c> run.</summary>
    public CliRun UnboundGraph { get; }

    /// <summary>The <c>diff</c> run.</summary>
    public CliRun Diff { get; }

    /// <summary>The <c>check --explain</c> run.</summary>
    public CliRun ExplainCheck { get; }

    /// <summary>The JSON report written with <c>--explain</c>.</summary>
    public string ExplainJson { get; }

    /// <summary>The SARIF log written with <c>--explain</c>.</summary>
    public string ExplainSarif { get; }

    /// <summary>The JSON report's text.</summary>
    public string Json { get; }

    /// <summary>The SARIF log's text.</summary>
    public string Sarif { get; }

    /// <summary>The bound graph's text.</summary>
    public string GraphJson { get; }

    /// <summary>The unbound graph's text.</summary>
    public string UnboundGraphJson { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(TempDirectory)) Directory.Delete(TempDirectory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }
    }
}

/// <summary>The xunit collection that shares one <see cref="OrdersFixture"/>.</summary>
[CollectionDefinition(Name)]
public sealed class OrdersCollection : ICollectionFixture<OrdersFixture>
{
    /// <summary>The collection's name.</summary>
    public const string Name = "orders-sample";
}
