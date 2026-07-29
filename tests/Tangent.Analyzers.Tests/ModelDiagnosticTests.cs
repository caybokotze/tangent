using System.Threading.Tasks;
using Xunit;

namespace Tangent.Analyzers.Tests;

public sealed class ModelDiagnosticTests
{
    private const string Source = """
namespace Sample.Api;

public sealed class OrdersController
{
}
""";

    [Fact]
    public async Task ParseErrorsAreReportedInTheContractFile()
    {
        const string contracts = """
contract Controller {
    match: class in Sample.Api.**
    sealed
""";

        await Harness.AnalyzerAsync(contracts, Source, Harness.Contract("TNG1000", 3, 11, 3, 11));
    }

    [Fact]
    public async Task UnknownContractInBindIsReportedInTheContractFile()
    {
        const string contracts = """
contract Controller {
    match: class in Sample.Api.** where name ends with "Controller"
    sealed
}

bind Sample.Api.OrdersController to Contoller
""";

        await Harness.AnalyzerAsync(contracts, Source, Harness.Contract("TNG1001", 6, 37, 6, 46));
    }

    [Fact]
    public async Task ContractThatIsNeverBoundIsReported()
    {
        const string contracts = """
contract Controller {
    match: class in Sample.Api.** where name ends with "Controller"
    sealed
}

contract Orphan {
    sealed
}
""";

        await Harness.AnalyzerAsync(contracts, Source, Harness.Contract("TNG1030", 6, 1, 8, 2));
    }

    [Fact]
    public async Task UnresolvableTypeNameIsReported()
    {
        const string contracts = """
contract Controller {
    match: class in Sample.Api.** where name ends with "Controller"
    inherits NoSuchBaseType
}
""";

        const string source = """
namespace Sample.Api;

public sealed class {|TNG0102:OrdersController|}
{
}
""";

        await Harness.AnalyzerAsync(contracts, source, Harness.Contract("TNG1020", 3, 5, 3, 28));
    }
}
