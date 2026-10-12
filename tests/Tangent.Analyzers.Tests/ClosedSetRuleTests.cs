using Xunit;

namespace Tangent.Analyzers.Tests;

/// <summary><c>implements only</c> and <c>inherits only</c>: TNG0110 and TNG0111.</summary>
public sealed class ClosedSetRuleTests
{
    private const string Contracts = """
contract Repository {
    match: class in Sample.Infrastructure.** where name ends with "Repository"
    implements only I{Name}
}
""";

    [Fact]
    public async Task ImplementingAnotherRepositorysInterfaceReports()
    {
        const string source = """
namespace Sample.Infrastructure;

public interface IOrderRepository { }

public interface ICustomerRepository { }

public sealed class {|TNG0110:OrderRepository|} : IOrderRepository, ICustomerRepository
{
}
""";

        await Harness.AnalyzerAsync(Contracts, source);
    }

    [Fact]
    public async Task ImplementingOnlyItsOwnInterfaceIsClean()
    {
        const string source = """
namespace Sample.Infrastructure;

public interface IOrderRepository { }

public sealed class OrderRepository : IOrderRepository
{
}
""";

        await Harness.AnalyzerAsync(Contracts, source);
    }

    [Fact]
    public async Task InterfacesInheritedByAnAllowedInterfaceAreAllowed()
    {
        // IOrderRepository extends IReadable, so the class implements IReadable too. The rule allows what
        // the allowed interface itself brings in, otherwise `implements only I{Name}` is unusable.
        const string source = """
namespace Sample.Infrastructure;

public interface IReadable { }

public interface IOrderRepository : IReadable { }

public sealed class OrderRepository : IOrderRepository, IReadable
{
}
""";

        await Harness.AnalyzerAsync(Contracts, source);
    }

    [Fact]
    public async Task ExtraInterfacesCanBeListed()
    {
        const string contracts = """
contract Repository {
    match: class in Sample.Infrastructure.** where name ends with "Repository"
    implements only I{Name}, IDisposable
}
""";

        const string source = """
using System;

namespace Sample.Infrastructure;

public interface IOrderRepository { }

public sealed class OrderRepository : IOrderRepository, IDisposable
{
    public void Dispose() { }
}
""";

        await Harness.AnalyzerAsync(contracts, source);
    }

    [Fact]
    public async Task InheritingAnotherBaseTypeReports()
    {
        const string contracts = """
contract Repository {
    match: class in Sample.Infrastructure.** where name ends with "Repository"
    inherits only RepositoryBase
}
""";

        const string source = """
namespace Sample.Infrastructure;

public abstract class RepositoryBase { }

public abstract class LegacyBase { }

public sealed class {|TNG0111:OrderRepository|} : LegacyBase
{
}
""";

        await Harness.AnalyzerAsync(contracts, source);
    }

    [Fact]
    public async Task NoBaseTypeOrTheAllowedBaseIsClean()
    {
        const string contracts = """
contract Repository {
    match: class in Sample.Infrastructure.** where name ends with "Repository"
    inherits only RepositoryBase
}
""";

        const string source = """
namespace Sample.Infrastructure;

public abstract class RepositoryBase { }

public sealed class OrderRepository : RepositoryBase
{
}

public sealed class CustomerRepository
{
}
""";

        await Harness.AnalyzerAsync(contracts, source);
    }
}

/// <summary>Naming and location as rules: TNG0112 and TNG0113.</summary>
public sealed class NameAndLocationRuleTests
{
    private const string Contracts = """
project "Sample" {
    layer Application = Sample.Application.**
    layer Domain      = Sample.Domain.**

    Application -> Domain
    Domain      -> nothing
}

contract Handler {
    match: any in Sample.** where implements IHandler and not abstract and not interface

    require name ends with "Handler"
    require lives in Application
}
""";

    [Fact]
    public async Task WrongNameAndWrongLayerBothReport()
    {
        // IHandler lives in Domain so that the Domain type below breaks only the location rule, not the
        // layer matrix as well.
        const string source = """
namespace Sample.Domain
{
    public interface IHandler { }

    public sealed class {|TNG0113:CancelHandler|} : IHandler { }
}

namespace Sample.Application
{
    public sealed class {|TNG0112:BookAppointment|} : Sample.Domain.IHandler { }
}
""";

        await Harness.AnalyzerAsync(Contracts, source);
    }

    [Fact]
    public async Task RightNameInTheRightLayerIsClean()
    {
        const string source = """
namespace Sample.Application;

public interface IHandler { }

public sealed class BookAppointmentHandler : IHandler { }
""";

        await Harness.AnalyzerAsync(Contracts, source);
    }

    [Fact]
    public async Task AbstractTypesAreExcludedByTheNegatedCondition()
    {
        const string source = """
namespace Sample.Application;

public interface IHandler { }

public abstract class HandlerBase : IHandler { }
""";

        await Harness.AnalyzerAsync(Contracts, source);
    }

    [Fact]
    public async Task MethodNamesCanBeRequired()
    {
        const string contracts = """
contract Handler {
    match: class in Sample.Application.** where name ends with "Handler"
    methods where public { require name ends with "Async" }
}
""";

        const string source = """
using System.Threading.Tasks;

namespace Sample.Application;

public sealed class BookHandler
{
    public Task {|TNG0112:Handle|}() => Task.CompletedTask;

    public Task RunAsync() => Task.CompletedTask;
}
""";

        await Harness.AnalyzerAsync(contracts, source);
    }
}

/// <summary>References by contract, and the project's allow list: TNG0204 and TNG0205.</summary>
public sealed class ContractReferenceRuleTests
{
    private const string Contracts = """
contract Repository {
    match: class in Sample.Infrastructure.** where name ends with "Repository"
}

contract Handler {
    match: class in Sample.Application.** where name ends with "Handler"
    forbid using contract Repository
}
""";

    [Fact]
    public async Task ReferenceToATypeBoundToTheForbiddenContractReports()
    {
        const string source = """
namespace Sample.Infrastructure
{
    public sealed class OrderRepository
    {
        public int Count() => 0;
    }
}

namespace Sample.Application
{
    public sealed class BookHandler
    {
        public int Handle()
        {
            var repository = {|TNG0204:new Sample.Infrastructure.OrderRepository()|};
            return repository.Count();
        }
    }
}
""";

        await Harness.AnalyzerAsync(Contracts, source);
    }

    [Fact]
    public async Task ReferenceToAnUnboundTypeInTheSameNamespaceIsClean()
    {
        // The rule follows the contract, not the namespace: SeedData is in Infrastructure but is bound
        // to nothing, so it is not a repository.
        const string source = """
namespace Sample.Infrastructure
{
    public sealed class SeedData
    {
        public int Count() => 0;
    }
}

namespace Sample.Application
{
    public sealed class BookHandler
    {
        public int Handle() => new Sample.Infrastructure.SeedData().Count();
    }
}
""";

        await Harness.AnalyzerAsync(Contracts, source);
    }

    [Fact]
    public async Task UndeclaredExternalNamespaceReportsWhenAnAllowListExists()
    {
        const string contracts = """
project "Sample" {
    layer Domain = Sample.Domain.**

    allow Microsoft.Extensions.**

    Domain -> nothing
}
""";

        const string source = """
namespace Outside
{
    public static class Clock
    {
        public static int Now => 0;
    }
}

namespace Sample.Domain
{
    public sealed class Appointment
    {
        public int StartsAt => {|TNG0205:Outside.Clock.Now|};
    }
}
""";

        await Harness.AnalyzerAsync(contracts, source);
    }

    [Fact]
    public async Task WithoutAnAllowListExternalNamespacesArePermitted()
    {
        const string contracts = """
project "Sample" {
    layer Domain = Sample.Domain.**

    Domain -> nothing
}
""";

        const string source = """
namespace Outside
{
    public static class Clock
    {
        public static int Now => 0;
    }
}

namespace Sample.Domain
{
    public sealed class Appointment
    {
        public int StartsAt => Outside.Clock.Now;
    }
}
""";

        await Harness.AnalyzerAsync(contracts, source);
    }

    [Fact]
    public async Task AnAllowedNamespaceIsClean()
    {
        const string contracts = """
project "Sample" {
    layer Domain = Sample.Domain.**

    allow Outside.**

    Domain -> nothing
}
""";

        const string source = """
namespace Outside
{
    public static class Clock
    {
        public static int Now => 0;
    }
}

namespace Sample.Domain
{
    public sealed class Appointment
    {
        public int StartsAt => Outside.Clock.Now;
    }
}
""";

        await Harness.AnalyzerAsync(contracts, source);
    }
}
