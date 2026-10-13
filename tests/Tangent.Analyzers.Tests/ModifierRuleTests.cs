using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Tangent.Analyzers.Tests;

public sealed class ModifierRuleTests
{
    private const string Contracts = """
contract Controller {
    match: class in Sample.Api.** where name ends with "Controller"
    sealed
}
""";

    [Fact]
    public async Task UnsealedBoundTypeReports()
    {
        const string source = """
namespace Sample.Api;

public class {|TNG0101:OrdersController|}
{
}
""";

        await Harness.AnalyzerAsync(Contracts, source);
    }

    [Fact]
    public async Task TypeLevelRefinementAppliesOnlyToMatchingTypes()
    {
        const string contracts = """
contract Api {
    match: class in Sample.Api.**
    where name ends with "Controller" { sealed }
}
""";

        const string source = """
namespace Sample.Api;

public class {|TNG0101:OrdersController|}
{
}

public class OrdersService
{
}
""";

        await Harness.AnalyzerAsync(contracts, source);
    }

    [Fact]
    public async Task SealedBoundTypeIsClean()
    {
        const string source = """
namespace Sample.Api;

public sealed class OrdersController
{
}
""";

        await Harness.AnalyzerAsync(Contracts, source);
    }

    [Fact]
    public async Task UnboundTypeIsClean()
    {
        const string source = """
namespace Sample.Api;

public class OrdersService
{
}
""";

        await Harness.AnalyzerAsync(Contracts, source);
    }

    [Fact]
    public async Task FieldModifiersAreChecked()
    {
        const string contracts = """
contract Service {
    match: class in Sample.Domain.**
    fields { require private; require readonly; forbid static }
}
""";

        const string source = """
namespace Sample.Domain;

public sealed class PricingService
{
    private readonly int _rate = 1;
    public readonly int {|TNG0101:Cached|} = 1;
    private int {|TNG0101:_count|} = 0;
    private static readonly int {|TNG0101:Shared|} = 2;
}
""";

        await Harness.AnalyzerAsync(contracts, source);
    }

    [Fact]
    public async Task MessagesReadMustBeAndMustNotBe()
    {
        const string contracts = """
contract Service {
    match: class in Sample.Domain.**
    methods { forbid static }
    fields { require readonly }
}
""";

        const string source = """
namespace Sample.Domain;

public sealed class PricingService
{
    private int {|#1:_count|} = 0;

    public static int {|#0:Rate|}() => 1;
}
""";

        await Harness.AnalyzerAsync(
            contracts,
            source,
            new DiagnosticResult(TangentDiagnostics.Get("TNG0101"))
                .WithLocation(1)
                .WithMessage("Service: field '_count' must be readonly. (rule Service/fields/modifier#0, contracts.tan:4)"),
            new DiagnosticResult(TangentDiagnostics.Get("TNG0101"))
                .WithLocation(0)
                .WithMessage("Service: method 'Rate' must not be static. (rule Service/methods/modifier#0, contracts.tan:3)"));
    }

    [Fact]
    public async Task ConstructorAccessibilityIsChecked()
    {
        const string contracts = """
contract Entity {
    match: class in Sample.Domain.Entities.**
    ctor { forbid public }
}
""";

        const string source = """
namespace Sample.Domain.Entities;

public sealed class Customer
{
    public {|TNG0101:Customer|}()
    {
    }
}
""";

        await Harness.AnalyzerAsync(contracts, source);
    }
}
