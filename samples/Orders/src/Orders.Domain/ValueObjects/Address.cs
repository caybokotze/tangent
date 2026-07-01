namespace Orders.Domain.ValueObjects;

public sealed record Address(string Line1, string City, string PostCode, string Country);
