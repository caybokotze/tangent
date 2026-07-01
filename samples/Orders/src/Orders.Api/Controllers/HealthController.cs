using Microsoft.AspNetCore.Mvc;

namespace Orders.Api.Controllers;

/// <summary>
/// Conforming, with one sanctioned gap: it has no [ProducesResponseType]. bindings.tan carries
///     bind Orders.Api.Controllers.HealthController to Controller except [ProducesResponseType]
/// so the missing attribute is expected to produce no diagnostic at all.
/// </summary>
[ApiController]
[Route("health")]
public sealed class HealthController : ApiControllerBase
{
    private readonly ILogger<HealthController> _logger;

    public HealthController(ILogger<HealthController> logger) => _logger = logger;

    [HttpGet]
    public Task<ActionResult<string>> GetAsync()
    {
        _logger.LogTrace("Health probe.");
        return Task.FromResult<ActionResult<string>>(Ok("healthy"));
    }
}
