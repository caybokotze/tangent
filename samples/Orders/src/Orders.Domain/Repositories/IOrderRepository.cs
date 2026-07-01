using Orders.Domain.Entities;

namespace Orders.Domain.Repositories;

public interface IOrderRepository
{
    Task<Order?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> ListAsync(int page, int size, CancellationToken cancellationToken = default);

    Task<Order> AddAsync(Order order, CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
