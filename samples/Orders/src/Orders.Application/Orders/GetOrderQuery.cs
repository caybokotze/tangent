using Orders.Application.Abstractions;
using Orders.Application.ViewModels;
using Orders.Domain.Repositories;
using Orders.Domain.Services;

namespace Orders.Application.Orders;

public sealed record GetOrderQuery(Guid OrderId) : IRequest<OrderViewModel?>;

/// <summary>CONFORMING: sealed, implements IRequestHandler, injects only allowed abstractions
/// and stores each of them in a private readonly field.</summary>
public sealed class GetOrderQueryHandler : IRequestHandler<GetOrderQuery, OrderViewModel?>
{
    private readonly IOrderRepository _orders;
    private readonly IPricingService _pricing;

    public GetOrderQueryHandler(IOrderRepository orders, IPricingService pricing)
    {
        _orders = orders;
        _pricing = pricing;
    }

    public async Task<OrderViewModel?> HandleAsync(GetOrderQuery request, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetAsync(request.OrderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var priced = _pricing.PriceFor(order);

        return new OrderViewModel(
            order.Id,
            order.CustomerId,
            order.Status.ToString(),
            priced.Amount,
            priced.Currency,
            [.. order.Lines.Select(line => new OrderLineViewModel(line.Id, line.Sku, line.Quantity, line.UnitPrice.Amount))]);
    }
}
