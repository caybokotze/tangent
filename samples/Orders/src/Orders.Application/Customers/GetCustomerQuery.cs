using Orders.Application.Abstractions;
using Orders.Application.ViewModels;
using Orders.Domain.Repositories;

namespace Orders.Application.Customers;

public sealed record GetCustomerQuery(Guid CustomerId) : IRequest<CustomerViewModel?>;

/// <summary>CONFORMING.</summary>
public sealed class GetCustomerQueryHandler : IRequestHandler<GetCustomerQuery, CustomerViewModel?>
{
    private readonly ICustomerRepository _customers;
    private readonly IUserContext _user;

    public GetCustomerQueryHandler(ICustomerRepository customers, IUserContext user)
    {
        _customers = customers;
        _user = user;
    }

    public async Task<CustomerViewModel?> HandleAsync(
        GetCustomerQuery request,
        CancellationToken cancellationToken = default)
    {
        if (!_user.IsInRole("sales"))
        {
            return null;
        }

        var customer = await _customers.GetAsync(request.CustomerId, cancellationToken);

        return customer is null
            ? null
            : new CustomerViewModel(customer.Id, customer.Name, customer.Email, customer.IsActive);
    }
}
