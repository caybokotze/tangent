namespace Orders.Domain;

/// <summary>The Domain never reads the system clock directly.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
