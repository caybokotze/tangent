using System.Collections.Immutable;

namespace Tangent.Benchmarks;

/// <summary>
/// The C# the analyzer benchmarks compile. Everything the sample contracts name — the Tangent attributes,
/// a miniature ASP.NET Core, MediatR and EF Core — is declared here, so the compilation needs nothing but
/// the framework reference assemblies.
/// </summary>
internal static class AnalyzerSources
{
    /// <summary>Stub framework types, so no package reference is needed.</summary>
    private const string Stubs = """
        namespace Tangent
        {
            [System.AttributeUsage(System.AttributeTargets.All, AllowMultiple = true)]
            public sealed class TangentAttribute : System.Attribute
            {
                public TangentAttribute(string contract) { Contract = contract; }
                public string Contract { get; }
            }

            [System.AttributeUsage(System.AttributeTargets.All, AllowMultiple = true)]
            public sealed class TangentExemptAttribute : System.Attribute
            {
                public TangentExemptAttribute(string reason) { Reason = reason; }
                public string Reason { get; }
            }
        }

        namespace Microsoft.AspNetCore.Mvc
        {
            public interface IActionResult { }
            public sealed class OkResult : IActionResult { }
            public sealed class ActionResult<TValue> { public ActionResult(TValue value) { Value = value; } public TValue Value { get; } }

            public sealed class ApiControllerAttribute : System.Attribute { }
            public sealed class RouteAttribute : System.Attribute { public RouteAttribute(string template) { Template = template; } public string Template { get; } }
            public sealed class HttpGetAttribute : System.Attribute { }
            public sealed class HttpPostAttribute : System.Attribute { }
            public sealed class HttpPutAttribute : System.Attribute { }
            public sealed class HttpDeleteAttribute : System.Attribute { }
            public sealed class HttpPatchAttribute : System.Attribute { }
            public sealed class ProducesResponseTypeAttribute : System.Attribute { public ProducesResponseTypeAttribute(int statusCode) { StatusCode = statusCode; } public int StatusCode { get; } }
        }

        namespace Orders.Api
        {
            public abstract class ApiControllerBase { }
        }

        namespace MediatR
        {
            public interface IMediator { System.Threading.Tasks.Task<TResponse> Send<TResponse>(object request); }
            public interface IRequestHandler<TRequest, TResponse> { System.Threading.Tasks.Task<TResponse> Handle(TRequest request); }
        }

        namespace Microsoft.Extensions.Logging
        {
            public interface ILogger<TCategory> { void Log(string message); }
        }

        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext { }
        }

        namespace Orders.Infrastructure.Persistence
        {
            public sealed class OrdersDbContext : Microsoft.EntityFrameworkCore.DbContext { }
        }
        """;

    /// <summary>A plain class in a namespace outside every declared layer, matched by no contract.</summary>
    private const string Unbound = """
        namespace Contoso.Plumbing
        {
            public sealed class CacheKeyBuilder
            {
                private readonly string _prefix;

                public CacheKeyBuilder(string prefix) { _prefix = prefix; }

                public string Build(string suffix) => _prefix + ":" + suffix;

                public int Length => _prefix.Length;
            }
        }
        """;

    /// <summary>A controller that satisfies every rule in the `Controller` contract.</summary>
    private const string Conforming = """
        using System.Threading.Tasks;
        using MediatR;
        using Microsoft.AspNetCore.Mvc;
        using Microsoft.Extensions.Logging;
        using Orders.Api;

        namespace Orders.Api.Controllers
        {
            public sealed class OrderDto { public int Id { get; init; } }

            [ApiController]
            [Route("api/orders")]
            public sealed class OrdersController : ApiControllerBase
            {
                private readonly IMediator _mediator;
                private readonly ILogger<OrdersController> _logger;

                public OrdersController(IMediator mediator, ILogger<OrdersController> logger)
                {
                    _mediator = mediator;
                    _logger = logger;
                }

                [HttpGet]
                [ProducesResponseType(200)]
                public Task<ActionResult<OrderDto>> Get(int id) =>
                    Task.FromResult(new ActionResult<OrderDto>(new OrderDto { Id = id }));

                [HttpPost]
                [ProducesResponseType(201)]
                public Task<IActionResult> Create(OrderDto dto)
                {
                    _logger.Log("creating");
                    return Task.FromResult<IActionResult>(new OkResult());
                }
            }
        }
        """;

    /// <summary>
    /// A controller that breaks the contract in several independent ways: not sealed, no attributes, a
    /// mutable injected field, a forbidden injected type, a wrong return type and a forbidden `new`.
    /// </summary>
    private const string Violating = """
        using System.Collections.Generic;
        using Microsoft.AspNetCore.Mvc;
        using Microsoft.EntityFrameworkCore;
        using Orders.Api;
        using Orders.Infrastructure.Persistence;

        namespace Orders.Api.Controllers
        {
            public class ReportsController : ApiControllerBase
            {
                private OrdersDbContext _db;

                internal ReportsController(OrdersDbContext db)
                {
                    _db = db;
                }

                public IEnumerable<string> GetSummaries() => new[] { "a", "b" };

                public DbContext Rebuild() => new DbContext();
            }
        }
        """;

    /// <summary>Every source, in compilation order.</summary>
    internal static ImmutableArray<string> All { get; } =
        ImmutableArray.Create(Stubs, Unbound, Conforming, Violating);
}
