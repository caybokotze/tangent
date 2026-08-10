using System.Threading.Tasks;
using Xunit;

namespace Tangent.Analyzers.Tests;

public sealed class FlowRuleTests
{
    [Fact]
    public async Task ParameterNotAssignedToPrivateReadonlyFieldReports()
    {
        const string contracts = """
contract Controller {
    match: class in Sample.Api.** where name ends with "Controller"
    ctor { require each parameter assigned to private readonly field }
}
""";

        const string source = """
namespace Sample.Api;

public sealed class OrdersController
{
    private readonly int _first;
    private int _second;

    public OrdersController(int first, int {|TNG0301:second|})
    {
        _first = first;
        _second = second;
    }
}
""";

        await Harness.AnalyzerAsync(contracts, source);
    }

    [Fact]
    public async Task ParameterNotNullCheckedReports()
    {
        const string contracts = """
contract Controller {
    match: class in Sample.Api.** where name ends with "Controller"
    ctor { require each parameter null-checked }
}
""";

        const string source = """
using System;

namespace Sample.Api;

public sealed class OrdersController
{
    private readonly string _a;
    private readonly string _b;
    private readonly string _c;
    private readonly int _n;

    public OrdersController(string a, string b, string {|TNG0302:c|}, int n)
    {
        ArgumentNullException.ThrowIfNull(a);
        _a = a;
        _b = b ?? throw new ArgumentNullException(nameof(b));
        _c = c;
        _n = n;
    }
}
""";

        await Harness.AnalyzerAsync(contracts, source);
    }

    [Fact]
    public async Task NullCheckByIfPatternCounts()
    {
        const string contracts = """
contract Controller {
    match: class in Sample.Api.** where name ends with "Controller"
    ctor { require each parameter null-checked }
}
""";

        const string source = """
using System;

namespace Sample.Api;

public sealed class OrdersController
{
    private readonly string _a;

    public OrdersController(string a)
    {
        if (a is null)
        {
            throw new ArgumentNullException(nameof(a));
        }

        _a = a;
    }
}
""";

        await Harness.AnalyzerAsync(contracts, source);
    }

    [Fact]
    public async Task ParameterNotStoredReports()
    {
        const string contracts = """
contract Command {
    match: class in Sample.Application.Commands.**
    ctor { require each parameter not stored }
}
""";

        const string source = """
namespace Sample.Application.Commands;

public sealed class CreateOrder
{
    private readonly int _kept;

    public CreateOrder(int {|TNG0301:kept|})
    {
        _kept = kept;
    }
}
""";

        await Harness.AnalyzerAsync(contracts, source);
    }

    [Fact]
    public async Task StatelessReportsMutableInstanceState()
    {
        const string contracts = """
contract DomainService {
    match: class in Sample.Domain.Services.**
    stateless
}
""";

        const string source = """
namespace Sample.Domain.Services;

public sealed class PricingService
{
    private readonly int _rate = 1;
    private int {|TNG0303:_calls|};
    private static int _total;

    public int {|TNG0303:Cache|} { get; set; }

    public int Rate => _rate;
}
""";

        await Harness.AnalyzerAsync(contracts, source);
    }

    [Fact]
    public async Task StatelessTypeIsClean()
    {
        const string contracts = """
contract DomainService {
    match: class in Sample.Domain.Services.**
    stateless
}
""";

        const string source = """
namespace Sample.Domain.Services;

public sealed class PricingService
{
    private readonly int _rate = 1;

    public int Rate { get; init; }

    public int Apply(int amount) => amount * _rate;
}
""";

        await Harness.AnalyzerAsync(contracts, source);
    }

    [Fact]
    public async Task PrimaryConstructorParameterMustBeCapturedInAField()
    {
        const string contracts = """
contract Handler {
    match: class in Sample.Application.** where name ends with "Handler"
    ctor { require each parameter assigned to private readonly field }
}
""";

        const string source = """
using Sample.Framework;

namespace Sample.Application;

public sealed class GoodHandler(IMediator mediator, IClock clock)
{
    private readonly IMediator _mediator = mediator;
    private readonly IClock _clock = clock;

    public int Run() => _clock is null ? 0 : 1;
}

public sealed class BadHandler(IMediator {|TNG0301:mediator|})
{
    public int Run() => mediator is null ? 0 : 1;
}
""";

        await Harness.AnalyzerAsync(contracts, source);
    }
}
