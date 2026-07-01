using Orders.Domain.ValueObjects;

namespace Orders.Domain.Services;

public interface ITaxCalculator
{
    Money AddTax(Money net);
}
