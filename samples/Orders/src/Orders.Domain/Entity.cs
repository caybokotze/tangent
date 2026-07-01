namespace Orders.Domain;

/// <summary>Identity-based equality for everything the Domain persists.</summary>
public abstract class Entity<TId> where TId : notnull
{
    protected Entity(TId id) => Id = id;

    public TId Id { get; }

    public override bool Equals(object? obj) =>
        obj is Entity<TId> other && GetType() == other.GetType() && EqualityComparer<TId>.Default.Equals(Id, other.Id);

    public override int GetHashCode() => Id.GetHashCode();
}
