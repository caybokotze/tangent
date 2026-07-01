using Microsoft.EntityFrameworkCore;
using Orders.Application.Abstractions;
using Orders.Application.Customers;
using Orders.Application.Orders;
using Orders.Application.ViewModels;
using Orders.Domain;
using Orders.Domain.Repositories;
using Orders.Domain.Services;
using Orders.Infrastructure;
using Orders.Infrastructure.Messaging;
using Orders.Infrastructure.Persistence;
using Orders.Infrastructure.Repositories;

namespace Orders.Api.DependencyInjection;

/// <summary>
/// The composition root. Every clean-architecture solution has one, and it is always the place
/// where the Api layer has to see Infrastructure.
/// </summary>
/// <remarks>
/// VIOLATION (TNG0202): project.tan says `Api -> Application, Domain`. Nothing here is allowed to
/// name an `Orders.Infrastructure.**` type. Tangent has no notion of "composition root" yet, and
/// that is deliberate: this is the case that forces the discussion.
/// </remarks>
public static class ServiceRegistration
{
    public static IServiceCollection AddOrdersServices(this IServiceCollection services)
    {
        // Provider-less on purpose: the sample is built, never run.
        services.AddScoped(_ => new OrdersDbContext(new DbContextOptionsBuilder<OrdersDbContext>().Options));

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IUserContext, StaticUserContext>();
        services.AddScoped<IMediator, Mediator>();

        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IStockLevelRepository, StockLevelRepository>();

        services.AddSingleton<ITaxCalculator, TaxCalculator>();
        services.AddSingleton<IPricingService, PricingService>();
        services.AddSingleton<IDiscountPolicyService, DiscountPolicyService>();

        services.AddScoped<IRequestHandler<GetOrderQuery, OrderViewModel?>, GetOrderQueryHandler>();
        services.AddScoped<IRequestHandler<ListOrdersQuery, IReadOnlyList<OrderViewModel>>, ListOrdersQueryHandler>();
        services.AddScoped<IRequestHandler<CreateOrderCommand, Guid>, CreateOrderCommandHandler>();
        services.AddScoped<IRequestHandler<GetCustomerQuery, CustomerViewModel?>, GetCustomerQueryHandler>();

        return services;
    }
}
