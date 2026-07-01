using Microsoft.EntityFrameworkCore;
using Orders.Domain.Entities;
using Orders.Domain.Repositories;
using Orders.Infrastructure.Persistence;

namespace Orders.Infrastructure.Repositories;

/// <summary>CONFORMING.</summary>
public sealed class CustomerRepository : ICustomerRepository
{
    private readonly OrdersDbContext _db;

    public CustomerRepository(OrdersDbContext db) => _db = db;

    public Task<Customer?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        _db.Customers.FirstOrDefaultAsync(customer => customer.Id == id, cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
        _db.Customers.AnyAsync(customer => customer.Id == id, cancellationToken);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _db.SaveChangesAsync(cancellationToken);
}
