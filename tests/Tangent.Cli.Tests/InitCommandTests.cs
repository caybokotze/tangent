using Xunit;

namespace Tangent.Cli.Tests;

/// <summary>
/// <c>tangent init</c> reads the solution's project files directly rather than loading MSBuild, so it is
/// fast and works on a solution that has never been restored — which is exactly the situation someone is
/// in when they run it.
/// </summary>
public sealed class InitCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tangent-init-" + Guid.NewGuid().ToString("n"));

    public InitCommandTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src", "Shop.Api"));
        Directory.CreateDirectory(Path.Combine(_root, "src", "Shop.Domain"));

        File.WriteAllText(Path.Combine(_root, "Shop.slnx"), """
            <Solution>
              <Folder Name="/src/">
                <Project Path="src/Shop.Api/Shop.Api.csproj" />
                <Project Path="src/Shop.Domain/Shop.Domain.csproj" />
              </Folder>
            </Solution>
            """);

        File.WriteAllText(Path.Combine(_root, "src", "Shop.Api", "Shop.Api.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        File.WriteAllText(Path.Combine(_root, "src", "Shop.Domain", "Shop.Domain.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><RootNamespace>Shop.Core.Domain</RootNamespace><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
    }

    [Fact]
    public void ScaffoldsAProjectFileWithOneLayerPerRootNamespace()
    {
        CliRun run = Cli.Run("init", _root, "--no-color");

        Assert.Equal(ExitCodes.Clean, run.ExitCode);

        string projectTan = File.ReadAllText(Path.Combine(_root, ".tangent", "project.tan"));

        Assert.Contains("project \"Shop\" {", projectTan, StringComparison.Ordinal);
        Assert.Contains("root: \"src/\"", projectTan, StringComparison.Ordinal);
        Assert.Contains("layer Api    = Shop.Api.**", projectTan, StringComparison.Ordinal);
        Assert.Contains("layer Domain = Shop.Core.Domain.**", projectTan, StringComparison.Ordinal);
        Assert.Contains("Api    -> nothing", projectTan, StringComparison.Ordinal);
        Assert.Contains("Domain -> nothing", projectTan, StringComparison.Ordinal);
        Assert.Contains("// contract Controller {", projectTan, StringComparison.Ordinal);

        Assert.Empty(File.ReadAllText(Path.Combine(_root, ".tangent", "contracts.tan")));
    }

    [Fact]
    public void RefusesToOverwriteWithoutForce()
    {
        Assert.Equal(ExitCodes.Clean, Cli.Run("init", _root, "--no-color").ExitCode);

        CliRun again = Cli.Run("init", _root, "--no-color");
        Assert.Equal(ExitCodes.LoadFailure, again.ExitCode);
        Assert.Contains("--force", again.Error, StringComparison.Ordinal);

        File.WriteAllText(Path.Combine(_root, ".tangent", "project.tan"), "// edited");
        Assert.Equal(ExitCodes.Clean, Cli.Run("init", _root, "--force", "--no-color").ExitCode);
        Assert.DoesNotContain("// edited", File.ReadAllText(Path.Combine(_root, ".tangent", "project.tan")), StringComparison.Ordinal);
    }

    [Fact]
    public void TheScaffoldBindsCleanly()
    {
        Assert.Equal(ExitCodes.Clean, Cli.Run("init", _root, "--no-color").ExitCode);

        CliRun lint = Cli.Run("lint", Path.Combine(_root, ".tangent"), "--no-color");
        Assert.Equal(ExitCodes.Clean, lint.ExitCode);
        Assert.Contains("0 contracts, 2 layers", lint.Out, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Nothing to do about a locked temp directory.
        }
    }
}
