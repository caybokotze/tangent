namespace Orders.Domain;

/// <summary>
/// Lives in Orders.Domain rather than Orders.Domain.Entities on purpose: the Entity contract
/// matches `class in Orders.Domain.Entities.**`, and a record is a different `kind` in the DSL.
/// Keeping it out of the matched namespace keeps the fixture unambiguous.
/// </summary>
public sealed record OrderConfirmed(Guid OrderId, DateTimeOffset OccurredAt) : IDomainEvent;
