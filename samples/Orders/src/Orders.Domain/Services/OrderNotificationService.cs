using Orders.Application.Notifications;
using Orders.Domain.Entities;

namespace Orders.Domain.Services;

public interface IOrderNotificationService
{
    void NotifyConfirmed(Order order);
}

/// <summary>
/// VIOLATIONS: does not implement <c>IOrderNotificationService</c>, and reaches into the
/// Application layer from the Domain.
/// </summary>
// VIOLATION (TNG0103): DomainService requires `implements I{Name}`.
public sealed class OrderNotificationService
{
    // VIOLATION (TNG0201 + TNG0202): Domain -> Application.
    private readonly INotificationDispatcher _dispatcher;

    public OrderNotificationService(INotificationDispatcher dispatcher) => _dispatcher = dispatcher;

    public void NotifyConfirmed(Order order) => _dispatcher.Dispatch($"order-confirmed:{order.Id}");
}
