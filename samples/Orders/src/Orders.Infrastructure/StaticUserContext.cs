using Orders.Application.Abstractions;

namespace Orders.Infrastructure;

public sealed class StaticUserContext : IUserContext
{
    public string UserId => "sample-user";

    public bool IsInRole(string role) => role is "sales" or "support";
}
