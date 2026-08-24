using Xunit;

namespace Tangent.Cli.Tests;

/// <summary>
/// <c>tangent diff --apply</c> writes through the MSBuild workspace. This runs against the real sample
/// (the only solution in the repository that is restored and loads) and puts every file back afterwards.
/// </summary>
[Collection(OrdersCollection.Name)]
public sealed class DiffApplyTests
{
    [Fact]
    public void WritesTheFixesAndNamesTheFiles()
    {
        string sourceRoot = Path.Combine(Cli.OrdersSample, "src");
        Dictionary<string, string> original = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllText, StringComparer.Ordinal);

        try
        {
            CliRun run = Cli.Run("diff", Cli.OrdersSample, "--apply", "--no-color");

            Assert.Equal(ExitCodes.Clean, run.ExitCode);
            Assert.Contains("wrote src/Orders.Api/Controllers/CustomersController.cs", run.Out, StringComparison.Ordinal);

            string controller = File.ReadAllText(Path.Combine(
                Cli.OrdersSample, "src", "Orders.Api", "Controllers", "CustomersController.cs"));

            Assert.Contains("public sealed class CustomersController", controller, StringComparison.Ordinal);
        }
        finally
        {
            foreach ((string path, string text) in original)
            {
                if (!string.Equals(File.ReadAllText(path), text, StringComparison.Ordinal))
                {
                    File.WriteAllText(path, text);
                }
            }
        }

        foreach ((string path, string text) in original)
        {
            Assert.Equal(text, File.ReadAllText(path));
        }
    }
}
