using Orders.Domain.Entities;
using Orders.Domain.ValueObjects;

namespace Orders.Domain.Services;

/// <summary>CONFORMING: sealed, implements IPricingService, stateless, only private readonly fields.</summary>
public sealed class PricingService : IPricingService
{
    private readonly ITaxCalculator _taxCalculator;

    public PricingService(ITaxCalculator taxCalculator) => _taxCalculator = taxCalculator;

    public Money PriceFor(Order order) => _taxCalculator.AddTax(order.Subtotal);
}
