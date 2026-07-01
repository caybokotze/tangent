namespace Orders.Domain.ValueObjects;

public readonly record struct Money(decimal Amount, string Currency)
{
    public static Money Zero(string currency) => new(0m, currency);

    public static Money operator +(Money left, Money right) =>
        left.Currency == right.Currency
            ? new Money(left.Amount + right.Amount, left.Currency)
            : throw new InvalidOperationException($"Cannot add {left.Currency} to {right.Currency}.");

    public static Money operator *(Money money, int quantity) => new(money.Amount * quantity, money.Currency);

    public override string ToString() => $"{Amount:0.00} {Currency}";
}
