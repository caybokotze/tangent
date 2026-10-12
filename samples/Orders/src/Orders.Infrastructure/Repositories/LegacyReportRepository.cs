using System.Data;

namespace Orders.Infrastructure.Repositories;

/// <summary>
/// Breaks almost every Repository rule, and is ignored wholesale in bindings.tan:
/// ignore Orders.Infrastructure.Repositories.LegacyReportRepository reason "migrating in Q4 2026".
/// Everything below is expected as TNG9000 (violation ignored), never as a warning.
/// </summary>
public class LegacyReportRepository
{
    private readonly string _connectionString;

    // IGNORED: not sealed, no I{Name} interface, injects a string rather than OrdersDbContext.
    public LegacyReportRepository(string connectionString) => _connectionString = connectionString;

    // IGNORED: public method that does not return Task of something.
    public DataTable Run(string sql)
    {
        var table = new DataTable("LegacyReport");
        table.Columns.Add("Query", typeof(string));
        table.Columns.Add("Source", typeof(string));
        table.Rows.Add(sql, _connectionString);
        return table;
    }
}
