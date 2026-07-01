using System.Data;

namespace Orders.Domain.Entities;

/// <summary>
/// VIOLATIONS: lives in <c>Orders.Domain.Entities</c> but inherits nothing, exposes a public field,
/// and reaches into <c>System.Data</c>.
/// </summary>
// VIOLATION (TNG0102): Entity requires `inherits Entity<*> | AggregateRoot<*>`.
public sealed class ShipmentNote
{
    // VIOLATION (TNG0108): Entity declares `forbid public fields`.
    public string Carrier = string.Empty;

    public Guid OrderId { get; init; }

    public string TrackingNumber { get; init; } = string.Empty;

    // VIOLATION (TNG0201): Entity declares `forbid using System.Data.**, Microsoft.**`.
    public DataTable ToLegacyTable()
    {
        var table = new DataTable("ShipmentNote");
        table.Columns.Add("OrderId", typeof(Guid));
        table.Columns.Add("Carrier", typeof(string));
        table.Rows.Add(OrderId, Carrier);
        return table;
    }
}
