using Orders.Application.Abstractions;
using Orders.Application.ViewModels;
using Orders.Domain.Repositories;
using Orders.Infrastructure.Caching;

namespace Orders.Application.Orders;

public sealed record ListOrdersQuery(int Page, int Size) : IRequest<IReadOnlyList<OrderViewModel>>;

/// <summary>
/// VIOLATIONS: reaches into the Infrastructure layer for a cache key, and parks a constructor
/// parameter in a mutable field.
/// </summary>
public sealed class ListOrdersQueryHandler : IRequestHandler<ListOrdersQuery, IReadOnlyList<OrderViewModel>>
{
    // VIOLATION (TNG0301): QueryHandler requires `each parameter assigned to private readonly field`.
    private IOrderRepository _orders;

    private string _lastCacheKey = string.Empty;

    public ListOrdersQueryHandler(IOrderRepository orders) => _orders = orders;

    public async Task<IReadOnlyList<OrderViewModel>> HandleAsync(
        ListOrdersQuery request,
        CancellationToken cancellationToken = default)
    {
        // VIOLATION (TNG0201 + TNG0202): Application -> Infrastructure.
        _lastCacheKey = CacheKeys.Orders(request.Page, request.Size);

        var orders = await _orders.ListAsync(request.Page, request.Size, cancellationToken);

        return
        [
            .. orders.Select(order => new OrderViewModel(
                order.Id,
                order.CustomerId,
                order.Status.ToString(),
                order.Subtotal.Amount,
                order.Currency,
                [.. order.Lines.Select(line => new OrderLineViewModel(line.Id, line.Sku, line.Quantity, line.UnitPrice.Amount))]))
        ];
    }
}
