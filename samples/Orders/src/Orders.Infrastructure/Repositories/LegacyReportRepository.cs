using System.Data;

namespace Orders.Infrastructure.Repositories;

/// <summary>
/// Breaks almost every Repository rule, and is exempted wholesale in bindings.tan:
/// exempt Orders.Infrastructure.Repositories.LegacyReportRepository reason "migrating in Q4 2026".
/// Everything below is expected as TNG9000 (exemption applied), never as a warning.
/// </summary>
public class LegacyReportRepository
{
    private readonly string _connectionString;

    // EXEMPTED: not sealed, no I{Name} interface, injects a string rather than OrdersDbContext.
    public LegacyReportRepository(string connectionString) => _connectionString = connectionString;

    // EXEMPTED: public method that does not return Task of something.
    public DataTable Run(string sql)
    {
        var table = new DataTable("LegacyReport");
        table.Columns.Add("Query", typeof(string));
        table.Columns.Add("Source", typeof(string));
        table.Rows.Add(sql, _connectionString);
        return table;
    }
}
