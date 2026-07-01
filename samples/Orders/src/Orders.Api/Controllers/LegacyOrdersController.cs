using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orders.Application.Abstractions;
using Orders.Infrastructure.Persistence;
using Tangent;

namespace Orders.Api.Controllers;

/// <summary>
/// The controller that new-s up everything it needs. Also demonstrates TNG0001: the contract
/// name in the [Tangent] attribute is misspelled.
/// </summary>
[ApiController]
[Route("api/legacy/orders")]
// VIOLATION (TNG0001): no contract named "Controler" exists. The fix offers "Controller".
[Tangent("Controler")]
public sealed class LegacyOrdersController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public LegacyOrdersController(IMediator mediator) => _mediator = mediator;

    [HttpGet("fetch")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    public async Task<ActionResult<string>> FetchAsync(CancellationToken cancellationToken)
    {
        // VIOLATION (TNG0203): `forbid new DbContext, new HttpClient`.
        using var http = new HttpClient();

        // VIOLATION (TNG0203): OrdersDbContext derives from DbContext.
        // VIOLATION (TNG0201 / TNG0202): Orders.Infrastructure.** and Microsoft.EntityFrameworkCore.**.
        using var db = new OrdersDbContext(new DbContextOptionsBuilder<OrdersDbContext>().Options);

        var payload = await http.GetStringAsync("https://example.invalid/orders", cancellationToken);
        return Ok(payload);
    }

    // VIOLATION (TNG0104): a public action with no [HttpGet | HttpPost | HttpPut | HttpDelete | HttpPatch].
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public Task<IActionResult> RebuildProjectionsAsync() =>
        Task.FromResult<IActionResult>(Accepted());
}
