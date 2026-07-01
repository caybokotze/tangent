using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orders.Application.ViewModels;
using Orders.Domain;
using Orders.Infrastructure.Persistence;

namespace Orders.Api.Controllers;

/// <summary>
/// The "we were in a hurry" controller. It queries the database directly and skips the mediator.
/// </summary>
[ApiController]
// VIOLATION (TNG0104): the Controller contract requires `attributes [ApiController], [Route(*)]`;
// there is no [Route(*)] here.
// VIOLATION (TNG0102): the Controller contract requires `inherits ApiControllerBase`.
public sealed class ReportsController : ControllerBase
{
    // VIOLATION (TNG0106 / TNG0201 / TNG0202): `injects only IMediator, ILogger<*>`,
    // `forbid using Orders.Infrastructure.**`, and Api may not depend on Infrastructure.
    private readonly OrdersDbContext _db;

    // VIOLATION (TNG0301): `each parameter assigned to private readonly field` - this one is mutable.
    private IClock _clock;

    public ReportsController(OrdersDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    // VIOLATION (TNG0105): must return Task<ActionResult<*>> | Task<IActionResult>.
    // VIOLATION (TNG0201): AsNoTracking comes from Microsoft.EntityFrameworkCore.**.
    [HttpGet("api/reports/summaries")]
    [ProducesResponseType(typeof(IEnumerable<ReportViewModel>), StatusCodes.Status200OK)]
    public IEnumerable<ReportViewModel> GetSummaries()
    {
        var asOf = _clock.UtcNow;

        var currencies = _db.Orders
            .AsNoTracking()
            .Select(order => order.Currency)
            .ToList();

        return [.. currencies.Select(currency => new ReportViewModel($"{currency}@{asOf:yyyy-MM-dd}", 1, 0m))];
    }
}
