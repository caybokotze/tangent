using Microsoft.EntityFrameworkCore;
using Orders.Domain.Entities;

namespace Orders.Infrastructure.Persistence;

/// <summary>
/// The only type in the solution that is allowed to be an EF Core <see cref="DbContext"/>.
/// The Controller contract forbids both `using Microsoft.EntityFrameworkCore.**`
/// and `new DbContext`, so every controller that touches this type is a violation.
/// </summary>
public sealed class OrdersDbContext : DbContext
{
    public OrdersDbContext(DbContextOptions<OrdersDbContext> options) : base(options)
    {
    }

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().Ignore(order => order.DomainEvents);
        modelBuilder.Entity<Order>().Ignore(order => order.Subtotal);
        modelBuilder.Entity<Customer>().Ignore(customer => customer.DomainEvents);
        modelBuilder.Entity<OrderLine>().Ignore(line => line.LineTotal);
        modelBuilder.Entity<AuditEntry>();
    }
}
