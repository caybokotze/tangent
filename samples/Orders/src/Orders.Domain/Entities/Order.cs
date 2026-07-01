using Orders.Domain.ValueObjects;

namespace Orders.Domain.Entities;

/// <summary>
/// CONFORMING against both <c>Entity</c> and <c>AggregateRoot</c>: private constructor plus a factory,
/// no public setters, no public fields, and no public method returning <c>void</c>.
/// </summary>
public sealed class Order : AggregateRoot<Guid>
{
    private readonly List<OrderLine> _lines = [];

    private Order(Guid id, Guid customerId, string currency, DateTimeOffset placedAt) : base(id)
    {
        CustomerId = customerId;
        Currency = currency;
        PlacedAt = placedAt;
        Status = OrderStatus.Draft;
    }

    public Guid CustomerId { get; private set; }

    public string Currency { get; private set; }

    public DateTimeOffset PlacedAt { get; private set; }

    public OrderStatus Status { get; private set; }

    public IReadOnlyList<OrderLine> Lines => _lines;

    public Money Subtotal => _lines.Aggregate(Money.Zero(Currency), (total, line) => total + line.LineTotal);

    public static Order Place(Guid customerId, string currency, DateTimeOffset placedAt) =>
        new(Guid.NewGuid(), customerId, currency, placedAt);

    public OrderLine AddLine(string sku, int quantity, Money unitPrice)
    {
        if (Status != OrderStatus.Draft)
        {
            throw new InvalidOperationException("Lines may only be added to a draft order.");
        }

        var line = new OrderLine(Guid.NewGuid(), sku, quantity, unitPrice);
        _lines.Add(line);
        return line;
    }

    public OrderStatus Confirm(DateTimeOffset confirmedAt)
    {
        if (_lines.Count == 0)
        {
            throw new InvalidOperationException("An order needs at least one line.");
        }

        Status = OrderStatus.Confirmed;
        Raise(new OrderConfirmed(Id, confirmedAt));
        return Status;
    }

    public OrderStatus Cancel()
    {
        Status = OrderStatus.Cancelled;
        return Status;
    }
}
