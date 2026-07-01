using Microsoft.AspNetCore.Mvc;
using Orders.Application.Abstractions;
using Orders.Application.Orders;
using Orders.Application.ViewModels;

namespace Orders.Api.Controllers;

/// <summary>
/// CONFORMING against every rule in `contract Controller`: sealed, inherits ApiControllerBase,
/// carries [ApiController] and [Route], injects only IMediator and ILogger, stores both in
/// private readonly fields, and every public action is attributed and returns an ActionResult task.
/// </summary>
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

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrderViewModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderViewModel>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Fetching order {OrderId}.", id);
        var order = await _mediator.SendAsync(new GetOrderQuery(id), cancellationToken);
        return FoundOrNotFound(order);
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<OrderViewModel>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<OrderViewModel>>> ListAsync(
        int page = 0,
        int size = 20,
        CancellationToken cancellationToken = default)
    {
        var orders = await _mediator.SendAsync(new ListOrdersQuery(page, size), cancellationToken);
        return Ok(orders);
    }

    [HttpPost]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Guid>> CreateAsync(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        var id = await _mediator.SendAsync(command, cancellationToken);
        return CreatedAtAction(nameof(GetByIdAsync), new { id }, id);
    }
}
