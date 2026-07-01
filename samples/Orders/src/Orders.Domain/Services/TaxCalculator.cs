using Orders.Domain.ValueObjects;

namespace Orders.Domain.Services;

/// <summary>CONFORMING: sealed, implements I{Name}, stateless, only private readonly fields.</summary>
public sealed class TaxCalculator : ITaxCalculator
{
    private readonly decimal _rate;

    public TaxCalculator() => _rate = 0.15m;

    public Money AddTax(Money net) => new(net.Amount * (1m + _rate), net.Currency);
}
