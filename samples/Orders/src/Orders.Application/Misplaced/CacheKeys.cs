// DELIBERATE VIOLATION SOURCE.
//
// Lives in the Orders.Application *project* but declares an Orders.Infrastructure *namespace*.
// Any Application type that touches it produces the Application -> Infrastructure layer
// violation (TNG0202) plus the contract-level `forbid using Orders.Infrastructure.**` (TNG0201).

namespace Orders.Infrastructure.Caching;

public static class CacheKeys
{
    public static string Orders(int page, int size) => $"orders:{page}:{size}";
}
