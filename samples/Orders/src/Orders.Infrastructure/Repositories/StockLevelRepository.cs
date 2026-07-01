using Orders.Domain;
using Orders.Infrastructure.Persistence;

namespace Orders.Infrastructure.Repositories;

/// <summary>
/// VIOLATION (TNG0109): the Repository contract says `interface I{Name} lives in Domain`,
/// but IStockLevelRepository is declared here, in the Infrastructure layer.
/// </summary>
public interface IStockLevelRepository
{
    IReadOnlyList<int> GetLevels(string sku);
}

/// <summary>
/// VIOLATIONS: not sealed, its interface is in the wrong layer, it takes an extra constructor
/// parameter, and its public method does not return a Task.
/// </summary>
// VIOLATION (TNG0101): Repository requires `sealed`.
public class StockLevelRepository : IStockLevelRepository
{
    private readonly OrdersDbContext _db;

    // VIOLATION (TNG0106): Repository declares `ctor { injects only OrdersDbContext }`.
    private readonly IClock _clock;

    public StockLevelRepository(OrdersDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    // VIOLATION (TNG0105): Repository declares `methods where public { returns Task<*> }`.
    public IReadOnlyList<int> GetLevels(string sku) => [sku.Length, _db.Orders.Count(), _clock.UtcNow.Hour];
}
