using Microsoft.EntityFrameworkCore;
using Orders.Domain.Entities;
using Orders.Domain.Repositories;
using Orders.Infrastructure.Persistence;

namespace Orders.Infrastructure.Repositories;

/// <summary>
/// CONFORMING: sealed, implements IOrderRepository (which lives in the Domain layer),
/// injects only OrdersDbContext, and every public method returns Task of something.
/// </summary>
public sealed class OrderRepository : IOrderRepository
{
    private readonly OrdersDbContext _db;

    public OrderRepository(OrdersDbContext db) => _db = db;

    public Task<Order?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        _db.Orders.Include(order => order.Lines).FirstOrDefaultAsync(order => order.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Order>> ListAsync(int page, int size, CancellationToken cancellationToken = default) =>
        await _db.Orders
            .OrderByDescending(order => order.PlacedAt)
            .Skip(page * size)
            .Take(size)
            .ToListAsync(cancellationToken);

    public async Task<Order> AddAsync(Order order, CancellationToken cancellationToken = default)
    {
        await _db.Orders.AddAsync(order, cancellationToken);
        return order;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _db.SaveChangesAsync(cancellationToken);
}
