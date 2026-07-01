using Orders.Domain.ValueObjects;

namespace Orders.Domain.Entities;

/// <summary>CONFORMING: inherits Entity&lt;*&gt;, no public constructor, no public setters, no public fields.</summary>
public sealed class OrderLine : Entity<Guid>
{
    internal OrderLine(Guid id, string sku, int quantity, Money unitPrice) : base(id)
    {
        Sku = sku;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public string Sku { get; }

    public int Quantity { get; private set; }

    public Money UnitPrice { get; }

    public Money LineTotal => UnitPrice * Quantity;

    internal OrderLine WithQuantity(int quantity)
    {
        Quantity = quantity;
        return this;
    }
}
