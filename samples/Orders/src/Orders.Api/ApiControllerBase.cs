using Microsoft.AspNetCore.Mvc;

namespace Orders.Api;

/// <summary>
/// The base every controller in this solution is required to inherit
/// (`contract Controller { inherits ApiControllerBase }`).
/// </summary>
public abstract class ApiControllerBase : ControllerBase
{
    protected ActionResult<T> FoundOrNotFound<T>(T? value) =>
        value is null ? NotFound() : Ok(value);
}
