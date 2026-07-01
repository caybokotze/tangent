namespace Orders.Application.Abstractions;

public interface IUserContext
{
    string UserId { get; }

    bool IsInRole(string role);
}
