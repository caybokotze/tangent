using System.Threading.Tasks;
using Xunit;

namespace Tangent.Analyzers.Tests;

public sealed class CodeFixTests
{
    [Fact]
    public async Task AddsSealed()
    {
        const string contracts = """
contract Controller {
    match: class in Sample.Api.** where name ends with "Controller"
    sealed
}
""";

        const string source = """
namespace Sample.Api;

public class {|TNG0101:OrdersController|}
{
}
""";

        const string fixedSource = """
namespace Sample.Api;

public sealed class OrdersController
{
}
""";

        await Harness.CodeFixAsync(contracts, source, fixedSource);
    }

    [Fact]
    public async Task AddsReadonlyToAField()
    {
        const string contracts = """
contract Service {
    match: class in Sample.Domain.**
    fields { require readonly }
}
""";

        const string source = """
namespace Sample.Domain;

public sealed class PricingService
{
    private int {|TNG0101:_count|} = 0;
}
""";

        const string fixedSource = """
namespace Sample.Domain;

public sealed class PricingService
{
    private readonly int _count = 0;
}
""";

        await Harness.CodeFixAsync(contracts, source, fixedSource);
    }

    [Fact]
    public async Task ReplacesPublicWithPrivateOnAForbiddenField()
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
}
""";

        const string fixedSource = """
namespace Sample.Domain.Entities;

public sealed class ShipmentNote
{
    private string Carrier = "";
}
""";

        await Harness.CodeFixAsync(contracts, source, fixedSource);
    }

    [Fact]
    public async Task AddsAMissingAttribute()
    {
        const string contracts = """
contract Controller {
    match: class in Sample.Api.** where name ends with "Controller"
    attributes [ApiController]
}
""";

        const string source = """
namespace Sample.Api;

public sealed class {|TNG0104:OrdersController|}
{
}
""";

        const string fixedSource = """
using Microsoft.AspNetCore.Mvc;

namespace Sample.Api;

[ApiController]
public sealed class OrdersController
{
}
""";

        await Harness.CodeFixAsync(contracts, source, fixedSource);
    }

    [Fact]
    public async Task AddsAMissingAttributeWithArguments()
    {
        const string contracts = """
contract Controller {
    match: class in Sample.Api.** where name ends with "Controller"
    methods where public { attributes [ProducesResponseType(*)] }
}
""";

        const string source = """
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Sample.Api;

public sealed class OrdersController
{
    public int {|TNG0104:Get|}() => 1;
}
""";

        const string fixedSource = """
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Sample.Api;

public sealed class OrdersController
{
    [ProducesResponseType(StatusCodes.Status200OK)]
    public int Get() => 1;
}
""";

        await Harness.CodeFixAsync(contracts, source, fixedSource);
    }

    [Fact]
    public async Task AddsAMissingBaseType()
    {
        const string contracts = """
contract Controller {
    match: class in Sample.Api.** where name ends with "Controller"
    inherits ApiControllerBase
}
""";

        const string source = """
using Sample.Framework;

namespace Sample.Api;

public sealed class {|TNG0102:OrdersController|}
{
}
""";

        const string fixedSource = """
using Sample.Framework;

namespace Sample.Api;

public sealed class OrdersController : ApiControllerBase
{
}
""";

        await Harness.CodeFixAsync(contracts, source, fixedSource);
    }

    [Fact]
    public async Task AddsAMissingBaseTypeAndItsUsing()
    {
        const string contracts = """
contract Controller {
    match: class in Sample.Api.** where name ends with "Controller"
    inherits ApiControllerBase
}
""";

        const string source = """
namespace Sample.Api;

public sealed class {|TNG0102:OrdersController|}
{
}
""";

        const string fixedSource = """
using Sample.Framework;

namespace Sample.Api;

public sealed class OrdersController : ApiControllerBase
{
}
""";

        await Harness.CodeFixAsync(contracts, source, fixedSource);
    }

    [Fact]
    public async Task AddsAMissingInterface()
    {
        const string contracts = """
contract DomainService {
    match: class in Sample.Domain.Services.**
    implements I{Name}
}
""";

        const string source = """
namespace Sample.Domain.Services;

public interface IPricingService { }

public sealed class {|TNG0103:PricingService|}
{
}
""";

        const string fixedSource = """
namespace Sample.Domain.Services;

public interface IPricingService { }

public sealed class PricingService : IPricingService
{
}
""";

        await Harness.CodeFixAsync(contracts, source, fixedSource);
    }

    [Fact]
    public async Task TurnsAPublicSetterIntoInit()
    {
        const string contracts = """
contract Entity {
    match: class in Sample.Domain.Entities.**
    properties { forbid public set }
}
""";

        const string source = """
namespace Sample.Domain.Entities;

public sealed class Customer
{
    public string Email { get; {|TNG0107:set|}; } = "";
}
""";

        const string fixedSource = """
namespace Sample.Domain.Entities;

public sealed class Customer
{
    public string Email { get; init; } = "";
}
""";

        await Harness.CodeFixAsync(contracts, source, fixedSource);
    }

    [Fact]
    public async Task FixAllAppliesEveryFixInOnePass()
    {
        const string contracts = """
contract Controller {
    match: class in Sample.Api.** where name ends with "Controller"
    sealed
}
""";

        const string source = """
namespace Sample.Api;

public class {|TNG0101:OrdersController|}
{
}

public class {|TNG0101:ReportsController|}
{
}
""";

        const string fixedSource = """
namespace Sample.Api;

public sealed class OrdersController
{
}

public sealed class ReportsController
{
}
""";

        await Harness.CodeFixAsync(contracts, source, fixedSource);
    }
}
