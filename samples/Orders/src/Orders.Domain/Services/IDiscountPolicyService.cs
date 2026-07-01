using Orders.Domain.Entities;

namespace Orders.Domain.Services;

public interface IDiscountPolicyService
{
    decimal DiscountFor(Customer customer);
}
