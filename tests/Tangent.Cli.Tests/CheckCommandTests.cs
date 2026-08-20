using System.Text.Json;
using Xunit;

namespace Tangent.Cli.Tests;

/// <summary>
/// End-to-end coverage of <c>tangent check</c> against <c>samples/Orders</c>. The expected result set is
/// <c>samples/Orders/VIOLATIONS.md</c>; where the counts here are larger than the table's 36 rows it is
/// because one documented break is reported at more than one site — see the assertions below.
/// </summary>
[Collection(OrdersCollection.Name)]
public sealed class CheckCommandTests(OrdersFixture fixture)
{
    /// <summary>
    /// Every code in VIOLATIONS.md, with the number of sites it is reported at. Reference violations
    /// (TNG0201, TNG0202) fire once per referencing member, and the analyzer deliberately reports a
    /// member signature and a member body separately, so these are above the table's row count.
    /// </summary>
    private static readonly Dictionary<string, int> Expected = new(StringComparer.Ordinal)
    {
        ["TNG0001"] = 1,   // LegacyOrdersController: [Tangent("Controler")]
        ["TNG0101"] = 7,   // sealed x2, Entity ctor, DomainService fields x4
        ["TNG0102"] = 2,   // ReportsController, ShipmentNote
        ["TNG0103"] = 1,   // OrderNotificationService
        ["TNG0104"] = 3,   // CustomersController, ReportsController, LegacyOrdersController
        ["TNG0105"] = 3,   // GetSummaries, GetLevels, Customer.Deactivate
        ["TNG0106"] = 3,   // ReportsController x2, StockLevelRepository
        ["TNG0107"] = 2,   // Customer.Email, DraftOrderViewModel.Id
        ["TNG0108"] = 1,   // ShipmentNote.Carrier
        ["TNG0109"] = 1,   // IStockLevelRepository
        ["TNG0201"] = 14,  // forbid using, once per referencing member
        ["TNG0202"] = 16,  // layer violations, once per referencing member (incl. generic args, locals, casts)
        ["TNG0203"] = 2,   // new HttpClient, new OrdersDbContext
        ["TNG0301"] = 2,   // ReportsController._clock, ListOrdersQueryHandler._orders
        ["TNG0303"] = 1,   // DiscountPolicyService
    };

    private JsonElement[] Rows => JsonDocument.Parse(fixture.Json).RootElement.EnumerateArray().ToArray();

    [Fact]
    public void ReportsExactlyTheDocumentedCodes()
    {
        Dictionary<string, int> actual = Rows
            .GroupBy(row => row.GetProperty("code").GetString()!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        Assert.Equal(Expected.Keys.OrderBy(k => k, StringComparer.Ordinal), actual.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach ((string code, int count) in Expected)
        {
            Assert.Equal(count, actual[code]);
        }

        Assert.Equal(Expected.Values.Sum(), Rows.Length);
    }

    [Theory]
    // Rows of VIOLATIONS.md, by (code, file, line).
    [InlineData("TNG0001", "src/Orders.Api/Controllers/LegacyOrdersController.cs", 16)]
    [InlineData("TNG0101", "src/Orders.Api/Controllers/CustomersController.cs", 15)]
    [InlineData("TNG0102", "src/Orders.Api/Controllers/ReportsController.cs", 16)]
    [InlineData("TNG0103", "src/Orders.Domain/Services/OrderNotificationService.cs", 16)]
    [InlineData("TNG0105", "src/Orders.Infrastructure/Repositories/StockLevelRepository.cs", 34)]
    [InlineData("TNG0107", "src/Orders.Application/ViewModels/DraftOrderViewModel.cs", 7)]
    [InlineData("TNG0108", "src/Orders.Domain/Entities/ShipmentNote.cs", 13)]
    [InlineData("TNG0109", "src/Orders.Infrastructure/Repositories/StockLevelRepository.cs", 20)]
    [InlineData("TNG0303", "src/Orders.Domain/Services/DiscountPolicyService.cs", 15)]
    public void PointsAtTheDocumentedSite(string code, string file, int line)
    {
        Assert.Contains(Rows, row =>
            row.GetProperty("code").GetString() == code &&
            row.GetProperty("file").GetString() == file &&
            row.GetProperty("line").GetInt32() == line);
    }

    [Fact]
    public void CarriesTheAnalyzerPropertyBag()
    {
        JsonElement sealedViolation = Rows.Single(row =>
            row.GetProperty("code").GetString() == "TNG0101" &&
            row.GetProperty("file").GetString() == "src/Orders.Api/Controllers/CustomersController.cs");

        Assert.Equal("Controller", sealedViolation.GetProperty("contract").GetString());
        Assert.Equal("Controller/type/modifier#0", sealedViolation.GetProperty("ruleId").GetString());
        Assert.Equal("Orders.Api", sealedViolation.GetProperty("project").GetString());
    }

    [Fact]
    public void HidesExemptionsUntilAsked()
    {
        Assert.DoesNotContain("TNG9000", fixture.Json, StringComparison.Ordinal);
        Assert.Contains("4 exemptions applied", fixture.Check.Out, StringComparison.Ordinal);

        CliRun shown = Cli.Run("check", Cli.OrdersSample, "--show-exemptions", "--no-color");
        Assert.Contains("TNG9000", shown.Out, StringComparison.Ordinal);
        Assert.Contains("migrating in Q4 2026", shown.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void GroupsByContract()
    {
        foreach (string heading in new[] { "Binding", "Controller", "Entity", "Repository", "ViewModel", "Layers" })
        {
            Assert.Contains("\n" + heading + "\n", "\n" + fixture.Check.Out.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FailOnWarningTurnsAWarningOnlyProjectRed()
    {
        // Orders.Domain has warnings but no errors, so it is the clean way to prove the --fail-on rule.
        CliRun lenient = Cli.Run("check", Cli.OrdersSample, "--project", "Orders.Domain", "--no-color");
        Assert.Equal(ExitCodes.Clean, lenient.ExitCode);

        CliRun strictish = Cli.Run("check", Cli.OrdersSample, "--project", "Orders.Domain", "--fail-on", "warning", "--no-color");
        Assert.Equal(ExitCodes.Violations, strictish.ExitCode);

        CliRun strict = Cli.Run("check", Cli.OrdersSample, "--project", "Orders.Domain", "--strict", "--no-color");
        Assert.Equal(ExitCodes.Violations, strict.ExitCode);
    }

    [Fact]
    public void TheWholeSolutionFailsBecauseOfTheMisspeltContract()
    {
        // TNG0001 is an error, so `check` over the whole sample exits 1 even at the default --fail-on error.
        Assert.Equal(ExitCodes.Violations, fixture.Check.ExitCode);
        Assert.Contains("(1 error, 58 warnings)", fixture.Check.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAPathThatIsNeitherSolutionNorProject()
    {
        CliRun run = Cli.Run("check", Path.Combine(Cli.RepositoryRoot, "docs"), "--no-color");
        Assert.Equal(ExitCodes.LoadFailure, run.ExitCode);
        Assert.Contains("no .sln, .slnx or .csproj file", run.Error, StringComparison.Ordinal);
    }
}
