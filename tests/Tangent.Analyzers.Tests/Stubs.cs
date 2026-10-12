namespace Tangent.Analyzers.Tests;

/// <summary>
/// The extra source document every test compiles alongside its own code: the two Tangent attributes
/// (declared inline so the tests do not depend on the Tangent.Attributes assembly) and a minimal
/// ASP.NET-like framework to hang realistic contracts off.
/// </summary>
internal static class Stubs
{
    internal const string Source = """
using System;

namespace Tangent
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Enum, AllowMultiple = true)]
    public sealed class TangentAttribute : Attribute
    {
        public TangentAttribute(string contract) { Contract = contract; }

        public string Contract { get; }
    }

    [AttributeUsage(
        AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Enum |
        AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Constructor | AttributeTargets.Field |
        AttributeTargets.Event,
        AllowMultiple = true)]
    public sealed class TangentIgnoreAttribute : Attribute
    {
        public TangentIgnoreAttribute(string reason) { Reason = reason; }

        public string Reason { get; }

        public string? Contract { get; init; }

        public string? Rule { get; init; }
    }
}

namespace Microsoft.AspNetCore.Mvc
{
    public interface IActionResult { }

    public class ActionResult<TValue> : IActionResult { }

    public abstract class ControllerBase
    {
        protected ActionResult<T> Ok<T>(T value) => new ActionResult<T>();
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ApiControllerAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class RouteAttribute : Attribute
    {
        public RouteAttribute(string template) { Template = template; }

        public string Template { get; }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HttpGetAttribute : Attribute
    {
        public HttpGetAttribute() { }

        public HttpGetAttribute(string template) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HttpPostAttribute : Attribute
    {
        public HttpPostAttribute() { }

        public HttpPostAttribute(string template) { }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class ProducesResponseTypeAttribute : Attribute
    {
        public ProducesResponseTypeAttribute(int statusCode) { }

        public ProducesResponseTypeAttribute(Type type, int statusCode) { }
    }
}

namespace Microsoft.AspNetCore.Http
{
    public static class StatusCodes
    {
        public const int Status200OK = 200;
    }
}

namespace Microsoft.Extensions.Logging
{
    public interface ILogger<out TCategory> { }
}

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
        public int SaveChanges() => 0;
    }
}

namespace Sample.Framework
{
    using Microsoft.AspNetCore.Mvc;

    public abstract class ApiControllerBase : ControllerBase { }

    public interface IMediator
    {
        TResponse Send<TResponse>(object request);
    }

    public interface IClock
    {
        DateTime UtcNow { get; }
    }

    public interface IRequestHandler<TRequest, TResponse>
    {
        TResponse Handle(TRequest request);
    }

    public abstract class Entity<TId> { }

    public abstract class AggregateRoot<TId> : Entity<TId> { }
}
""";
}
