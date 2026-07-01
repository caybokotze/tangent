namespace Orders.Domain.Entities;

/// <summary>
/// An append-only log row. It breaks the <c>Entity</c> contract on purpose and is exempted in
/// <c>bindings.tan</c>: <c>exempt Orders.Domain.Entities.AuditEntry from Entity reason "..."</c>.
/// Every violation below is expected to be reported as TNG9000 (exemption applied), not as a warning.
/// </summary>
public sealed class AuditEntry : Entity<long>
{
    // EXEMPTED: public constructor.
    public AuditEntry(long id) : base(id)
    {
    }

    // EXEMPTED: public setters.
    public string Action { get; set; } = string.Empty;

    public string Actor { get; set; } = string.Empty;

    public DateTimeOffset At { get; set; }
}
