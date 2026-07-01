using Orders.Application.Abstractions;
using Orders.Domain;
using Orders.Domain.Entities;
using Orders.Domain.Repositories;
using Orders.Domain.ValueObjects;

namespace Orders.Application.Orders;

public sealed record NewOrderLine(string Sku, int Quantity, decimal UnitPrice);

public sealed record CreateOrderCommand(Guid CustomerId, string Currency, IReadOnlyList<NewOrderLine> Lines)
    : IRequest<Guid>;

/// <summary>CONFORMING.</summary>
public sealed class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, Guid>
{
    private readonly IOrderRepository _orders;
    private readonly ICustomerRepository _customers;
    private readonly IClock _clock;

    public CreateOrderCommandHandler(IOrderRepository orders, ICustomerRepository customers, IClock clock)
    {
        _orders = orders;
        _customers = customers;
        _clock = clock;
    }

    public async Task<Guid> HandleAsync(CreateOrderCommand request, CancellationToken cancellationToken = default)
    {
        if (!await _customers.ExistsAsync(request.CustomerId, cancellationToken))
        {
            throw new InvalidOperationException($"Unknown customer {request.CustomerId}.");
        }

        var order = Order.Place(request.CustomerId, request.Currency, _clock.UtcNow);

        foreach (var line in request.Lines)
        {
            order.AddLine(line.Sku, line.Quantity, new Money(line.UnitPrice, request.Currency));
        }

        order.Confirm(_clock.UtcNow);

        await _orders.AddAsync(order, cancellationToken);
        await _orders.SaveChangesAsync(cancellationToken);

        return order.Id;
    }
}
