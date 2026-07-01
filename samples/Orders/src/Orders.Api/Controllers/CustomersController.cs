using Microsoft.AspNetCore.Mvc;
using Orders.Application.Abstractions;
using Orders.Application.Customers;
using Orders.Application.ViewModels;
using Tangent;

namespace Orders.Api.Controllers;

/// <summary>
/// Mostly conforming. Two deliberate breaks plus one violation that is legitimately exempted.
/// </summary>
[ApiController]
[Route("api/customers")]
// VIOLATION (TNG0101): the Controller contract requires `sealed`.
public class CustomersController : ApiControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<CustomersController> _logger;

    public CustomersController(IMediator mediator, ILogger<CustomersController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    // VIOLATION (TNG0104): no [ProducesResponseType(*)].
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CustomerViewModel>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Fetching customer {CustomerId}.", id);
        var customer = await _mediator.SendAsync(new GetCustomerQuery(id), cancellationToken);
        return FoundOrNotFound(customer);
    }

    // EXEMPTED: returns Task<string> rather than Task<ActionResult<*>> | Task<IActionResult>.
    // The attribute suppresses the rule and is reported as TNG9000 instead.
    [HttpGet("export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [TangentExempt("CSV export writes straight to the response; the ActionResult wrapper lands in v2", Contract = "Controller")]
    public Task<string> ExportAsync(CancellationToken cancellationToken) =>
        Task.FromResult("id,name,email");
}
