using Xunit;

namespace Tangent.Cli.Tests;

/// <summary>
/// <c>tangent lint</c> never needs a compilation to do its main job, so these tests point it straight at
/// the <c>.tangent</c> folder and stay fast.
/// </summary>
public sealed class LintCommandTests
{
    private static string TangentFolder => Path.Combine(Cli.OrdersSample, ".tangent");

    [Fact]
    public void BindsTheSampleCleanly()
    {
        CliRun run = Cli.Run("lint", TangentFolder, "--no-color");

        Assert.Equal(ExitCodes.Clean, run.ExitCode);
        Assert.Contains("7 contracts, 4 layers, 6 files", run.Out, StringComparison.Ordinal);
        Assert.Contains("0 errors, 0 warnings", run.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void SaysSoWhenItCannotResolveTypeNames()
    {
        CliRun run = Cli.Run("lint", TangentFolder, "--no-color");
        Assert.Contains("TNG1020 skipped", run.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void DumpIrNamesEveryContract()
    {
        CliRun run = Cli.Run("lint", TangentFolder, "--dump-ir", "--no-color");

        Assert.Equal(ExitCodes.Clean, run.ExitCode);
        Assert.Contains("\"project\": \"Orders\"", run.Out, StringComparison.Ordinal);

        foreach (string contract in new[] { "Controller", "Repository", "Entity", "AggregateRoot", "DomainService", "ViewModel", "QueryHandler" })
        {
            Assert.Contains("\"name\": \"" + contract + "\"", run.Out, StringComparison.Ordinal);
        }

        Assert.Contains("\"id\": \"Controller/type/modifier#0\"", run.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void DumpAstPrintsEveryFile()
    {
        CliRun run = Cli.Run("lint", TangentFolder, "--dump-ast", "--no-color");

        Assert.Equal(ExitCodes.Clean, run.ExitCode);
        Assert.Contains("=== .tangent/project.tan ===", run.Out, StringComparison.Ordinal);
        Assert.Contains("=== .tangent/controllers.tan ===", run.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsModelErrorsAndExitsTwo()
    {
        string directory = Path.Combine(Path.GetTempPath(), "tangent-lint-" + Guid.NewGuid().ToString("n"), ".tangent");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "broken.tan"), "contract Controller extends Nope {\n    require sealed\n}\n");

            CliRun run = Cli.Run("lint", directory, "--no-color");

            Assert.Equal(ExitCodes.ModelError, run.ExitCode);
            Assert.Contains("TNG1001", run.Out, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(directory)!, recursive: true);
        }
    }

    [Fact]
    public void StrictPromotesWarningsToErrors()
    {
        string directory = Path.Combine(Path.GetTempPath(), "tangent-lint-" + Guid.NewGuid().ToString("n"), ".tangent");
        Directory.CreateDirectory(directory);
        try
        {
            // A contract nothing can ever bind to is TNG1030, a warning.
            File.WriteAllText(Path.Combine(directory, "orphan.tan"), "contract Orphan {\n    require sealed\n}\n");

            Assert.Equal(ExitCodes.Clean, Cli.Run("lint", directory, "--no-color").ExitCode);
            Assert.Equal(ExitCodes.ModelError, Cli.Run("lint", directory, "--strict", "--no-color").ExitCode);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(directory)!, recursive: true);
        }
    }

    [Fact]
    public void ResolvesTypeNamesWhenTheSolutionIsThere()
    {
        CliRun run = Cli.Run("lint", Cli.OrdersSample, "--no-color");

        Assert.Equal(ExitCodes.Clean, run.ExitCode);
        Assert.DoesNotContain("TNG1020 skipped", run.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("TNG1020", run.Out, StringComparison.Ordinal);
    }
}
