using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Tangent.Analyzers.Tests;

public sealed class ExemptionTests
{
    private const string Contracts = """
contract Controller {
    match: class in Sample.Api.** where name ends with "Controller"
    sealed
    methods where public { attributes [HttpGet] }
}
""";

    [Fact]
    public async Task WholeTypeExemptionAttributeSuppressesEverything()
    {
        const string source = """
using Tangent;

namespace Sample.Api;

[{|TNG9000:TangentExempt("legacy, migrating in Q4")|}]
public class LegacyController
{
    public int Get() => 1;
}
""";

        await Harness.AnalyzerAsync(Contracts, source);
    }

    [Fact]
    public async Task ContractScopedExemptionOnAMemberSuppressesThatMember()
    {
        const string source = """
using Microsoft.AspNetCore.Mvc;
using Tangent;

namespace Sample.Api;

public sealed class OrdersController
{
    [HttpGet]
    public int Get() => 1;

    [{|TNG9000:TangentExempt("exported by a scheduled job", Contract = "Controller")|}]
    public int Export() => 2;
}
""";

        await Harness.AnalyzerAsync(Contracts, source);
    }

    [Fact]
    public async Task RuleScopedExemptionOnAMemberSuppressesOnlyThatRule()
    {
        const string source = """
using Microsoft.AspNetCore.Mvc;
using Tangent;

namespace Sample.Api;

public sealed class OrdersController
{
    [{|TNG9000:TangentExempt("no verb by design", Rule = "Controller/methods/attributes#0")|}]
    public int Export() => 2;
}
""";

        await Harness.AnalyzerAsync(Contracts, source);
    }

    [Fact]
    public async Task ExemptStatementSuppressesTheWholeType()
    {
        const string contracts = """
contract Controller {
    match: class in Sample.Api.** where name ends with "Controller"
    sealed
}

exempt Sample.Api.LegacyController reason "migrating in Q4 2026"
""";

        const string source = """
namespace Sample.Api;

public class {|TNG9000:LegacyController|}
{
}
""";

        await Harness.AnalyzerAsync(contracts, source);
    }

    [Fact]
    public async Task ExemptStatementScopedToOneContractLeavesOthers()
    {
        const string contracts = """
contract Controller {
    match: class in Sample.Api.** where name ends with "Controller"
    sealed
}

exempt Sample.Api.LegacyController from Controller reason "legacy"
""";

        const string source = """
namespace Sample.Api;

public class {|TNG9000:LegacyController|}
{
}
""";

        await Harness.AnalyzerAsync(contracts, source);
    }

    [Fact]
    public async Task BindExceptAttributeRemovesTheAttributeRule()
    {
        const string contracts = """
contract Controller {
    match: class in Sample.Nowhere.**
    sealed
    methods where public { attributes [HttpGet] }
}

bind Sample.Api.HealthController to Controller except [HttpGet]
""";

        const string source = """
namespace Sample.Api;

public sealed class {|TNG9000:HealthController|}
{
    public int Get() => 1;
}
""";

        await Harness.AnalyzerAsync(contracts, source);
    }

    [Fact]
    public async Task BindExceptRuleIdRemovesThatRule()
    {
        const string contracts = """
contract Controller {
    match: class in Sample.Nowhere.**
    sealed
    methods where public { attributes [HttpGet] }
}

bind Sample.Api.HealthController to Controller except "Controller/type/modifier#0"
""";

        const string source = """
using Microsoft.AspNetCore.Mvc;

namespace Sample.Api;

public class {|TNG9000:HealthController|}
{
    [HttpGet]
    public int Get() => 1;
}
""";

        await Harness.AnalyzerAsync(contracts, source);
    }

    [Fact]
    public async Task BindExceptAttributeNotesTheRemovedRuleAsAnExemption()
    {
        const string contracts = """
contract Controller {
    match: class in Sample.Nowhere.**
    sealed
    methods where public { attributes [ProducesResponseType] }
}

bind Sample.Api.HealthController to Controller except [ProducesResponseType]
""";

        const string source = """
namespace Sample.Api;

public sealed class HealthController
{
    public int Get() => 1;
}
""";

        // Hidden, so only --show-exemptions surfaces it, but it is reported: every exemption is visible.
        DiagnosticResult expected = new DiagnosticResult("TNG9000", DiagnosticSeverity.Hidden)
            .WithSpan("/0/Test0.cs", 3, 21, 3, 37)
            .WithArguments("Exemption applied: bind except [ProducesResponseType] (contracts.tan:7)");

        await Harness.AnalyzerAsync(contracts, source, expected);
    }

    [Fact]
    public async Task BindExceptRuleIdNotesTheRemovedRuleAsAnExemption()
    {
        const string contracts = """
contract Controller {
    match: class in Sample.Nowhere.**
    sealed
    methods where public { attributes [HttpGet] }
}

bind Sample.Api.HealthController to Controller except "Controller/type/modifier#0"
""";

        const string source = """
using Microsoft.AspNetCore.Mvc;

namespace Sample.Api;

public class HealthController
{
    [HttpGet]
    public int Get() => 1;
}
""";

        DiagnosticResult expected = new DiagnosticResult("TNG9000", DiagnosticSeverity.Hidden)
            .WithSpan("/0/Test0.cs", 5, 14, 5, 30)
            .WithArguments("Exemption applied: bind except Controller/type/modifier#0 (contracts.tan:7)");

        await Harness.AnalyzerAsync(contracts, source, expected);
    }

    [Fact]
    public async Task BindExceptNotesEveryRemovedRuleOnce()
    {
        const string contracts = """
contract Controller {
    match: class in Sample.Nowhere.**
    sealed
    methods where public { attributes [HttpGet] }
}

bind Sample.Api.HealthController to Controller except "Controller/type/modifier#0", [HttpGet]
""";

        const string source = """
namespace Sample.Api;

public class {|TNG9000:HealthController|}
{
    public int Get() => 1;
}
""";

        // One note per (type, removed rule): the modifier rule and the attribute rule, both at the
        // type's identifier.
        DiagnosticResult second = new DiagnosticResult("TNG9000", DiagnosticSeverity.Hidden)
            .WithSpan("/0/Test0.cs", 3, 14, 3, 30)
            .WithArguments("Exemption applied: bind except [HttpGet] (contracts.tan:7)");

        await Harness.AnalyzerAsync(contracts, source, second);
    }
}

public sealed class RefinementTests
{
    [Fact]
    public async Task ExtendingContractAddsRulesWithoutDuplicatingInheritedOnes()
    {
        const string contracts = """
contract Entity {
    match: class in Sample.Domain.Entities.**
    sealed
}

contract Aggregate extends Entity {
    match: class in Sample.Domain.Entities.** where name ends with "Aggregate"
    methods where public { forbid returns void }
}
""";

        const string source = """
namespace Sample.Domain.Entities;

public class {|TNG0101:OrderAggregate|}
{
    public void {|TNG0105:Apply|}() { }
}
""";

        await Harness.AnalyzerAsync(contracts, source);
    }

    [Fact]
    public async Task RefinementNarrowsTheSelector()
    {
        const string contracts = """
contract ViewModel {
    match: class in Sample.Application.ViewModels.**
    properties where name ends with "Id" { forbid public set }
}
""";

        const string source = """
namespace Sample.Application.ViewModels;

public sealed class OrderViewModel
{
    public string OrderId { get; {|TNG0107:set|}; } = "";

    public string Name { get; set; } = "";
}
""";

        await Harness.AnalyzerAsync(contracts, source);
    }

    [Fact]
    public async Task CombinedRefinementConditionsNarrowFurther()
    {
        const string contracts = """
contract Entity {
    match: class in Sample.Domain.Entities.**
    methods where public and name starts with "Set" { forbid returns void }
}
""";

        const string source = """
namespace Sample.Domain.Entities;

public sealed class Order
{
    public void {|TNG0105:SetName|}() { }

    public void Rename() { }

    private void SetCode() { }
}
""";

        await Harness.AnalyzerAsync(contracts, source);
    }
}
