namespace Orders.Application.ViewModels;

/// <summary>CONFORMING: positional record, public get-only properties, no fields.</summary>
public sealed record OrderViewModel(
    Guid Id,
    Guid CustomerId,
    string Status,
    decimal Subtotal,
    string Currency,
    IReadOnlyList<OrderLineViewModel> Lines);

/// <summary>CONFORMING.</summary>
public sealed record OrderLineViewModel(Guid Id, string Sku, int Quantity, decimal UnitPrice);
