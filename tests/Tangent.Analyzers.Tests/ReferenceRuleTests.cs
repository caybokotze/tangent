using System.Threading.Tasks;
using Xunit;

namespace Tangent.Analyzers.Tests;

public sealed class ForbiddenUsingTests
{
    private const string Contracts = """
contract Controller {
    match: class in Sample.Api.** where name ends with "Controller"
    forbid using Sample.Infrastructure.**
}
""";

    [Fact]
    public async Task ReferenceIntoForbiddenNamespaceReports()
    {
        const string source = """
namespace Sample.Infrastructure
{
    public sealed class SampleDbContext
    {
        public int SaveChanges() => 0;
    }
}

namespace Sample.Api
{
    public sealed class OrdersController
    {
        public int Count()
        {
            var db = {|TNG0201:new Sample.Infrastructure.SampleDbContext()|};
            return db.SaveChanges();
        }
    }
}
""";

        await Harness.AnalyzerAsync(Contracts, source);
    }

    [Fact]
    public async Task MemberSignatureIntoForbiddenNamespaceReports()
    {
        const string source = """
namespace Sample.Infrastructure
{
    public sealed class SampleDbContext { }
}

namespace Sample.Api
{
    using Sample.Infrastructure;

    public sealed class ReportsController
    {
        private readonly SampleDbContext {|TNG0201:_db|} = null!;
    }
}
""";

        await Harness.AnalyzerAsync(Contracts, source);
    }

    [Fact]
    public async Task ReferencesOutsideTheGlobAreClean()
    {
        const string source = """
namespace Sample.Application
{
    public static class Rates
    {
        public static int Value() => 1;
    }
}

namespace Sample.Api
{
    public sealed class OrdersController
    {
        public int Count() => Sample.Application.Rates.Value();
    }
}
""";

        await Harness.AnalyzerAsync(Contracts, source);
    }
}

public sealed class ForbiddenNewTests
{
    private const string Contracts = """
contract Controller {
    match: class in Sample.Api.** where name ends with "Controller"
    methods where public { forbid new DbContext }
}
""";

    [Fact]
    public async Task DerivedTypeIsAlsoForbidden()
    {
        const string source = """
using Microsoft.EntityFrameworkCore;

namespace Sample.Api;

public sealed class SampleDbContext : DbContext { }

public sealed class OrdersController
{
    public int Count()
    {
        var db = {|TNG0203:new SampleDbContext()|};
        return db.SaveChanges();
    }
}
""";

        await Harness.AnalyzerAsync(Contracts, source);
    }

    [Fact]
    public async Task NonPublicMethodIsNotSelected()
    {
        const string source = """
using Microsoft.EntityFrameworkCore;

namespace Sample.Api;

public sealed class OrdersController
{
    private int Count()
    {
        var db = new DbContext();
        return db.SaveChanges();
    }
}
""";

        await Harness.AnalyzerAsync(Contracts, source);
    }
}

public sealed class LayerRuleTests
{
    private const string Contracts = """
project "Sample" {
    layer Api            = Sample.Api.**
    layer Application    = Sample.Application.**
    layer Domain         = Sample.Domain.**
    layer Infrastructure = Sample.Infrastructure.**

    Api            -> Application, Domain
    Application    -> Domain
    Infrastructure -> Application, Domain
    Domain         -> nothing
}
""";

    [Fact]
    public async Task DomainMayNotReachApplication()
    {
        const string source = """
namespace Sample.Application
{
    public static class Rates
    {
        public static int Value() => 1;
    }
}

namespace Sample.Domain
{
    public sealed class OrderService
    {
        public int Total() => {|TNG0202:Sample.Application.Rates.Value()|};
    }
}
""";

        await Harness.AnalyzerAsync(Contracts, source);
    }

    [Fact]
    public async Task AllowedDirectionIsClean()
    {
        const string source = """
namespace Sample.Domain
{
    public static class Rates
    {
        public static int Value() => 1;
    }
}

namespace Sample.Application
{
    public sealed class OrderHandler
    {
        public int Total() => Sample.Domain.Rates.Value();
    }
}
""";

        await Harness.AnalyzerAsync(Contracts, source);
    }

    [Fact]
    public async Task SystemNamespacesAreImplicitlyAllowed()
    {
        const string source = """
using System;
using System.Collections.Generic;

namespace Sample.Domain;

public sealed class OrderService
{
    public IReadOnlyList<string> Names()
    {
        var list = new List<string> { Guid.NewGuid().ToString() };
        return list;
    }
}
""";

        await Harness.AnalyzerAsync(Contracts, source);
    }

    [Fact]
    public async Task UnlayeredNamespacesAreAllowed()
    {
        const string source = """
using Sample.Framework;

namespace Sample.Domain;

public sealed class OrderService
{
    private readonly IClock _clock;

    public OrderService(IClock clock) => _clock = clock;
}
""";

        await Harness.AnalyzerAsync(Contracts, source);
    }

    [Fact]
    public async Task UnboundTypesInALayerAreStillChecked()
    {
        const string source = """
namespace Sample.Infrastructure
{
    public static class Registry
    {
        public static int Count() => 1;
    }
}

namespace Sample.Api
{
    public static class ServiceRegistration
    {
        public static int Add() => {|TNG0202:Sample.Infrastructure.Registry.Count()|};
    }
}
""";

        await Harness.AnalyzerAsync(Contracts, source);
    }
}

/// <summary>
/// The Tier 1 walk covers every written type reference in a body, not only calls and object creation:
/// type tests, patterns, casts, <c>as</c>, local declarations and generic type arguments all resolve into
/// the namespace they name.
/// </summary>
public sealed class ReferenceWalkCoverageTests
{
    private const string UsingContracts = """
contract Controller {
    match: class in Sample.Api.** where name ends with "Controller"
    forbid using Sample.Infrastructure.**
}
""";

    private const string LayerContracts = """
project "Sample" {
    layer Api         = Sample.Api.**
    layer Application = Sample.Application.**
    layer Domain      = Sample.Domain.**

    Api         -> Application, Domain
    Application -> Domain
    Domain      -> nothing
}
""";

    /// <summary>A controller body, in a project whose contract forbids reaching into Sample.Infrastructure.</summary>
    private static string Forbidden(string body) => $$"""
namespace Sample.Infrastructure
{
    public sealed class SampleDbContext { }
}

namespace Sample.Api
{
    public static class Boxes
    {
        public static T Unwrap<T>() where T : class => null!;
    }

    public sealed class OrdersController
    {
{{body}}
    }
}
""";

    /// <summary>A Domain body, in a project whose layer matrix forbids Domain -&gt; Application.</summary>
    private static string Layered(string body) => $$"""
namespace Sample.Application
{
    public sealed class RateTable { }
}

namespace Sample.Domain
{
    public static class Boxes
    {
        public static T Unwrap<T>() where T : class => null!;
    }

    public sealed class OrderService
    {
{{body}}
    }
}
""";

    [Fact]
    public async Task IsTypeTestReports() =>
        await Harness.AnalyzerAsync(UsingContracts, Forbidden("""
        public bool Check(object value) => {|TNG0201:value is Sample.Infrastructure.SampleDbContext|};
"""));

    [Fact]
    public async Task IsPatternReports() =>
        await Harness.AnalyzerAsync(UsingContracts, Forbidden("""
        public bool Check(object value) => value is {|TNG0201:Sample.Infrastructure.SampleDbContext db|} && db is not null;
"""));

    [Fact]
    public async Task ExplicitCastReports() =>
        await Harness.AnalyzerAsync(UsingContracts, Forbidden("""
        public object Cast(object value) => {|TNG0201:(Sample.Infrastructure.SampleDbContext)value|};
"""));

    [Fact]
    public async Task AsCastReports() =>
        await Harness.AnalyzerAsync(UsingContracts, Forbidden("""
        public object? As(object value) => {|TNG0201:value as Sample.Infrastructure.SampleDbContext|};
"""));

    [Fact]
    public async Task LocalDeclarationReports() =>
        await Harness.AnalyzerAsync(UsingContracts, Forbidden("""
        public int Local()
        {
            {|TNG0201:Sample.Infrastructure.SampleDbContext|} db = null!;
            return db is null ? 0 : 1;
        }
"""));

    [Fact]
    public async Task GenericTypeArgumentReports() =>
        await Harness.AnalyzerAsync(UsingContracts, Forbidden("""
        public void Generic() => {|TNG0201:Boxes.Unwrap<Sample.Infrastructure.SampleDbContext>()|};
"""));

    [Fact]
    public async Task LayeredIsTypeTestReports() =>
        await Harness.AnalyzerAsync(LayerContracts, Layered("""
        public bool Check(object value) => {|TNG0202:value is Sample.Application.RateTable|};
"""));

    [Fact]
    public async Task LayeredIsPatternReports() =>
        await Harness.AnalyzerAsync(LayerContracts, Layered("""
        public bool Check(object value) => value is {|TNG0202:Sample.Application.RateTable table|} && table is not null;
"""));

    [Fact]
    public async Task LayeredExplicitCastReports() =>
        await Harness.AnalyzerAsync(LayerContracts, Layered("""
        public object Cast(object value) => {|TNG0202:(Sample.Application.RateTable)value|};
"""));

    [Fact]
    public async Task LayeredAsCastReports() =>
        await Harness.AnalyzerAsync(LayerContracts, Layered("""
        public object? As(object value) => {|TNG0202:value as Sample.Application.RateTable|};
"""));

    [Fact]
    public async Task LayeredLocalDeclarationReports() =>
        await Harness.AnalyzerAsync(LayerContracts, Layered("""
        public int Local()
        {
            {|TNG0202:Sample.Application.RateTable|} table = null!;
            return table is null ? 0 : 1;
        }
"""));

    [Fact]
    public async Task LayeredGenericTypeArgumentReports() =>
        await Harness.AnalyzerAsync(LayerContracts, Layered("""
        public void Generic() => {|TNG0202:Boxes.Unwrap<Sample.Application.RateTable>()|};
"""));

    [Fact]
    public async Task ArrayCreationElementTypeReports() =>
        await Harness.AnalyzerAsync(UsingContracts, Forbidden("""
        public object Array() => {|TNG0201:new Sample.Infrastructure.SampleDbContext[1]|};
"""));

    [Fact]
    public async Task DefaultExpressionReports() =>
        await Harness.AnalyzerAsync(UsingContracts, Forbidden("""
        public object? Blank() => {|TNG0201:default(Sample.Infrastructure.SampleDbContext)|};
"""));

    [Fact]
    public async Task TypeOfInsideATypeAttributeReports() =>
        await Harness.AnalyzerAsync(UsingContracts, """
namespace Sample.Infrastructure
{
    public sealed class SampleDbContext { }
}

namespace Sample.Api
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class ModelAttribute : System.Attribute
    {
        public ModelAttribute(System.Type type) { }
    }

    [{|TNG0201:Model(typeof(Sample.Infrastructure.SampleDbContext))|}]
    public sealed class OrdersController
    {
    }
}
""");

    [Fact]
    public async Task VarLocalFromAnAllowedCallDoesNotDoubleReport() =>
        await Harness.AnalyzerAsync(LayerContracts, """
namespace Sample.Domain
{
    public static class Rates
    {
        public static int Value() => 1;
    }
}

namespace Sample.Application
{
    public sealed class OrderHandler
    {
        public int Total()
        {
            var value = Sample.Domain.Rates.Value();
            return value;
        }
    }
}
""");
}
