using Orders.Domain.Entities;

namespace Orders.Domain.Services;

/// <summary>
/// VIOLATIONS: carries mutable instance state and a public static counter, against
/// <c>stateless</c> and <c>fields { require private; require readonly; forbid static }</c>.
/// </summary>
public sealed class DiscountPolicyService : IDiscountPolicyService
{
    // VIOLATION (TNG0101): fields must be private, readonly and non-static.
    public static int TotalEvaluations;

    // VIOLATION (TNG0303 + TNG0101): mutable instance field, so the service is not stateless.
    private int _evaluations;

    public decimal DiscountFor(Customer customer)
    {
        _evaluations++;
        TotalEvaluations++;
        return customer.IsActive && _evaluations > 10 ? 0.05m : 0m;
    }
}
