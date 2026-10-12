namespace Orders.Domain.Entities;

/// <summary>
/// An append-only log row. It breaks the <c>Entity</c> contract on purpose and is ignored in
/// <c>bindings.tan</c>: <c>ignore Orders.Domain.Entities.AuditEntry from Entity reason "..."</c>.
/// Every violation below is expected to be reported as TNG9000 (violation ignored), not as a warning.
/// </summary>
public sealed class AuditEntry : Entity<long>
{
    // IGNORED: public constructor.
    public AuditEntry(long id) : base(id)
    {
    }

    // IGNORED: public setters.
    public string Action { get; set; } = string.Empty;

    public string Actor { get; set; } = string.Empty;

    public DateTimeOffset At { get; set; }
}
