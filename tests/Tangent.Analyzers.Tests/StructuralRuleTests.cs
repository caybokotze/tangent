using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Tangent.Analyzers.Tests;

public sealed class InheritsRuleTests
{
    private const string Contracts = """
contract Controller {
    match: class in Sample.Api.** where name ends with "Controller"
    inherits ApiControllerBase
}
""";

    [Fact]
    public async Task MissingBaseTypeReports()
    {
        const string source = """
namespace Sample.Api;

public sealed class {|TNG0102:ReportsController|}
{
}
""";

        await Harness.AnalyzerAsync(Contracts, source);
    }

    [Fact]
    public async Task InheritedBaseTypeIsClean()
    {
        const string source = """
using Sample.Framework;

namespace Sample.Api;

public sealed class OrdersController : ApiControllerBase
{
}
""";

        await Harness.AnalyzerAsync(Contracts, source);
    }

    [Fact]
    public async Task GenericBaseTypeMatchesWildcardArgument()
    {
        const string contracts = """
contract Entity {
    match: class in Sample.Domain.Entities.**
    inherits Entity<*> | AggregateRoot<*>
}
""";

        const string source = """
using System;
using Sample.Framework;

namespace Sample.Domain.Entities;

public sealed class Order : AggregateRoot<Guid>
{
}

public sealed class {|TNG0102:ShipmentNote|}
{
}
""";

        await Harness.AnalyzerAsync(contracts, source);
    }
}

public sealed class ImplementsRuleTests
{
    private const string Contracts = """
contract DomainService {
    match: class in Sample.Domain.Services.**
    implements I{Name}
}
""";

    [Fact]
    public async Task CompanionInterfaceTemplateIsExpanded()
    {
        const string source = """
namespace Sample.Domain.Services;

public interface IPricingService { }

public sealed class PricingService : IPricingService { }

public sealed class {|TNG0103:TaxCalculator|} { }
""";

        await Harness.AnalyzerAsync(Contracts, source);
    }

    [Fact]
    public async Task TemplateIsExpandedInTheMessage()
    {
        const string source = """
namespace Sample.Domain.Services;

public sealed class {|#0:TaxCalculator|} { }
""";

        await Harness.AnalyzerAsync(
            Contracts,
            source,
            new DiagnosticResult(TangentDiagnostics.Get("TNG0103"))
                .WithLocation(0)
                .WithMessage(
                    "DomainService: type must implement ITaxCalculator. " +
                    "(rule DomainService/type/implements#0, contracts.tan:3)"));
    }

    [Fact]
    public async Task GenericInterfaceIsMatchedStructurally()
    {
        const string contracts = """
contract Handler {
    match: class in Sample.Application.** where name ends with "Handler"
    implements IRequestHandler<*, *>
}
""";

        const string source = """
using Sample.Framework;

namespace Sample.Application;

public sealed class GetOrderHandler : IRequestHandler<string, int>
{
    public int Handle(string request) => 0;
}

public sealed class {|TNG0103:ListOrdersHandler|}
{
}
""";

        await Harness.AnalyzerAsync(contracts, source);
    }
}

public sealed class AttributeRuleTests
{
    private const string Contracts = """
contract Controller {
    match: class in Sample.Api.** where name ends with "Controller"
    attributes [ApiController], [Route(*)]

    methods where public {
        attributes [HttpGet | HttpPost]
    }
}
""";

    [Fact]
    public async Task MissingTypeAttributeReports()
    {
        const string source = """
using Microsoft.AspNetCore.Mvc;

namespace Sample.Api;

[ApiController]
public sealed class {|TNG0104:ReportsController|}
{
}
""";

        await Harness.AnalyzerAsync(Contracts, source);
    }

    [Fact]
    public async Task MissingMethodAttributeReports()
    {
        const string source = """
using Microsoft.AspNetCore.Mvc;

namespace Sample.Api;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController
{
    [HttpGet]
    public int Get() => 1;

    public int {|TNG0104:Rebuild|}() => 2;
}
""";

        await Harness.AnalyzerAsync(Contracts, source);
    }

    [Fact]
    public async Task EitherAlternativeSatisfiesTheGroup()
    {
        const string source = """
using Microsoft.AspNetCore.Mvc;

namespace Sample.Api;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController
{
    [HttpPost]
    public int Create() => 1;
}
""";

        await Harness.AnalyzerAsync(Contracts, source);
    }
}

public sealed class ReturnsRuleTests
{
    [Fact]
    public async Task WrongReturnTypeReports()
    {
        const string contracts = """
contract Controller {
    match: class in Sample.Api.** where name ends with "Controller"
    methods where public { returns ActionResult<*> | IActionResult }
}
""";

        const string source = """
using Microsoft.AspNetCore.Mvc;

namespace Sample.Api;

public sealed class OrdersController
{
    public ActionResult<int> Get() => new ActionResult<int>();

    public int {|TNG0105:Count|}() => 1;
}
""";

        await Harness.AnalyzerAsync(contracts, source);
    }

    [Fact]
    public async Task ForbidReturnsVoidMatchesVoidMethods()
    {
        const string contracts = """
contract Aggregate {
    match: class in Sample.Domain.Entities.**
    methods where public { forbid returns void }
}
""";

        const string source = """
namespace Sample.Domain.Entities;

public sealed class Customer
{
    public void {|TNG0105:Deactivate|}() { }

    public int Activate() => 1;
}
""";

        await Harness.AnalyzerAsync(contracts, source);
    }
}

public sealed class InjectsRuleTests
{
    private const string Contracts = """
contract Controller {
    match: class in Sample.Api.** where name ends with "Controller"
    ctor { injects only IMediator, ILogger<*> }
}
""";

    [Fact]
    public async Task DisallowedParameterReports()
    {
        const string source = """
using Microsoft.Extensions.Logging;
using Sample.Framework;

namespace Sample.Api;

public sealed class OrdersController
{
    public OrdersController(IMediator mediator, ILogger<OrdersController> logger, IClock {|TNG0106:clock|})
    {
    }
}
""";

        await Harness.AnalyzerAsync(Contracts, source);
    }

    [Fact]
    public async Task AllowedParametersAreClean()
    {
        const string source = """
using Microsoft.Extensions.Logging;
using Sample.Framework;

namespace Sample.Api;

public sealed class OrdersController
{
    public OrdersController(IMediator mediator, ILogger<OrdersController> logger)
    {
    }
}
""";

        await Harness.AnalyzerAsync(Contracts, source);
    }

    [Fact]
    public async Task InjectsWithoutOnlyRequiresThePattern()
    {
        const string contracts = """
contract Handler {
    match: class in Sample.Application.** where name ends with "Handler"
    ctor { injects IMediator }
}
""";

        const string source = """
using Sample.Framework;

namespace Sample.Application;

public sealed class GoodHandler
{
    public GoodHandler(IMediator mediator) { }
}

public sealed class BadHandler
{
    public {|TNG0106:BadHandler|}(IClock clock) { }
}
""";

        await Harness.AnalyzerAsync(contracts, source);
    }
}

public sealed class AccessorRuleTests
{
    private const string Contracts = """
contract Entity {
    match: class in Sample.Domain.Entities.**
    properties { forbid public set }
}
""";

    [Fact]
    public async Task PublicSetterReports()
    {
        const string source = """
namespace Sample.Domain.Entities;

public sealed class Customer
{
    public string Name { get; init; } = "";

    public string Email { get; {|TNG0107:set|}; } = "";

    public string Code { get; private set; } = "";
}
""";

        await Harness.AnalyzerAsync(Contracts, source);
    }

    [Fact]
    public async Task RequireGetReportsMissingGetter()
    {
        const string contracts = """
contract ViewModel {
    match: class in Sample.Application.ViewModels.**
    properties { require get }
}
""";

        const string source = """
namespace Sample.Application.ViewModels;

public sealed class OrderViewModel
{
    private string _value = "";

    public string Name { get; set; } = "";

    public string {|TNG0107:Write|} { set { _value = value; } }
}
""";

        await Harness.AnalyzerAsync(contracts, source);
    }
}

public sealed class MemberKindRuleTests
{
    [Fact]
    public async Task ForbidPublicFieldsReportsTheField()
    {
        const string contracts = """
contract Entity {
    match: class in Sample.Domain.Entities.**
    forbid public fields
}
""";

        const string source = """
namespace Sample.Domain.Entities;

public sealed class ShipmentNote
{
    public string {|TNG0108:Carrier|} = "";

    private string _tracking = "";
}
""";

        await Harness.AnalyzerAsync(contracts, source);
    }

    [Fact]
    public async Task ForbidStaticMethodsReportsTheMethod()
    {
        const string contracts = """
contract Service {
    match: class in Sample.Domain.Services.**
    forbid static methods
}
""";

        const string source = """
namespace Sample.Domain.Services;

public sealed class PricingService
{
    public static int {|TNG0108:Compute|}() => 1;

    public int Apply() => 2;
}
""";

        await Harness.AnalyzerAsync(contracts, source);
    }
}

public sealed class InterfaceLivesInRuleTests
{
    private const string Contracts = """
project "Sample" {
    layer Domain         = Sample.Domain.**
    layer Infrastructure = Sample.Infrastructure.**

    Infrastructure -> Domain
    Domain         -> nothing
}

contract Repository {
    match: class in Infrastructure where name ends with "Repository"
    interface I{Name} lives in Domain
}
""";

    [Fact]
    public async Task InterfaceInTheWrongLayerReports()
    {
        const string source = """
namespace Sample.Domain
{
    public interface IOrderRepository { }
}

namespace Sample.Infrastructure
{
    using Sample.Domain;

    public interface IStockRepository { }

    public sealed class OrderRepository : IOrderRepository { }

    public sealed class {|TNG0109:StockRepository|} : IStockRepository { }
}
""";

        await Harness.AnalyzerAsync(Contracts, source);
    }
}
