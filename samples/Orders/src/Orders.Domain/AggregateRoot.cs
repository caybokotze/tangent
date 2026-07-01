namespace Orders.Domain;

public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}

/// <summary>A consistency boundary. Only aggregate roots are loaded and saved by repositories.</summary>
public abstract class AggregateRoot<TId> : Entity<TId> where TId : notnull
{
    private readonly List<IDomainEvent> _events = [];

    protected AggregateRoot(TId id) : base(id)
    {
    }

    public IReadOnlyList<IDomainEvent> DomainEvents => _events;

    protected void Raise(IDomainEvent domainEvent) => _events.Add(domainEvent);
}
