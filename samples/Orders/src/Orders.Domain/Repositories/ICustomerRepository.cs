using Orders.Domain.Entities;

namespace Orders.Domain.Repositories;

public interface ICustomerRepository
{
    Task<Customer?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
