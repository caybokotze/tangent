using Xunit;

namespace Tangent.Cli.Tests;

/// <summary><c>tangent diff</c> must never touch the working tree unless <c>--apply</c> says so.</summary>
[Collection(OrdersCollection.Name)]
public sealed class DiffCommandTests(OrdersFixture fixture)
{
    [Fact]
    public void PrintsUnifiedDiffHeadersAndHunks()
    {
        Assert.Contains("--- a/src/Orders.Api/Controllers/CustomersController.cs", fixture.Diff.Out, StringComparison.Ordinal);
        Assert.Contains("+++ b/src/Orders.Api/Controllers/CustomersController.cs", fixture.Diff.Out, StringComparison.Ordinal);
        Assert.Matches(@"@@ -\d+,\d+ \+\d+,\d+ @@", fixture.Diff.Out);
    }

    [Fact]
    public void ShowsTheKnownFixableViolations()
    {
        Assert.Contains("+public sealed class CustomersController", fixture.Diff.Out, StringComparison.Ordinal);
        Assert.Contains("+    [ProducesResponseType", fixture.Diff.Out, StringComparison.Ordinal);
        Assert.Contains("+public sealed class OrderNotificationService : IOrderNotificationService", fixture.Diff.Out, StringComparison.Ordinal);
        Assert.Contains("+public sealed class ReportsController : ApiControllerBase", fixture.Diff.Out, StringComparison.Ordinal);
        Assert.Contains("+    public Guid Id { get; init; }", fixture.Diff.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void ExitsNonZeroWhenThereIsSomethingToWrite()
    {
        Assert.Equal(ExitCodes.Violations, fixture.Diff.ExitCode);
        Assert.Matches(@"\d+ fixes in \d+ files", fixture.Diff.Out);
    }

    [Fact]
    public void LeavesTheSampleUntouched()
    {
        string[] files =
        [
            Path.Combine(Cli.OrdersSample, "src", "Orders.Api", "Controllers", "CustomersController.cs"),
            Path.Combine(Cli.OrdersSample, "src", "Orders.Domain", "Entities", "Customer.cs"),
            Path.Combine(Cli.OrdersSample, "src", "Orders.Application", "ViewModels", "DraftOrderViewModel.cs"),
        ];

        foreach (string file in files)
        {
            Assert.Contains("VIOLATION", File.ReadAllText(file), StringComparison.Ordinal);
        }

        Assert.Contains("public class CustomersController", File.ReadAllText(files[0]), StringComparison.Ordinal);
    }
}
