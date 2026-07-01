using Orders.Domain.ValueObjects;

namespace Orders.Domain.Entities;

/// <summary>
/// VIOLATIONS: public constructor (Entity: <c>ctor { forbid public }</c>), a public property setter
/// (Entity: <c>properties { forbid public set }</c>) and a public method returning <c>void</c>
/// (AggregateRoot: <c>methods where public { forbid returns void }</c>).
/// </summary>
public sealed class Customer : AggregateRoot<Guid>
{
    // VIOLATION (TNG0101): Entity requires `ctor { forbid public }` - construction goes through a factory.
    public Customer(Guid id, string name, Address billingAddress) : base(id)
    {
        Name = name;
        BillingAddress = billingAddress;
    }

    public string Name { get; private set; }

    // VIOLATION (TNG0107): Entity forbids public setters on properties.
    public string Email { get; set; } = string.Empty;

    public Address BillingAddress { get; private set; }

    public bool IsActive { get; private set; } = true;

    // VIOLATION (TNG0105): AggregateRoot forbids public methods returning void.
    public void Deactivate() => IsActive = false;

    public Customer Rename(string name)
    {
        Name = name;
        return this;
    }
}
