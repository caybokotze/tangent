namespace Orders.Domain.Entities;

public enum OrderStatus
{
    Draft = 0,
    Confirmed = 1,
    Shipped = 2,
    Cancelled = 3,
}
