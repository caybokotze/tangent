using Orders.Domain.Entities;
using Orders.Domain.ValueObjects;

namespace Orders.Domain.Services;

public interface IPricingService
{
    Money PriceFor(Order order);
}
