namespace Orders.Application.ViewModels;

/// <summary>CONFORMING.</summary>
public sealed record CustomerViewModel(Guid Id, string Name, string Email, bool IsActive);
