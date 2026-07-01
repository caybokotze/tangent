using Orders.Application.Abstractions;

namespace Orders.Infrastructure.Messaging;

/// <summary>A 20-line mediator. The sample needs the shape, not the package.</summary>
public sealed class Mediator : IMediator
{
    private readonly IServiceProvider _provider;

    public Mediator(IServiceProvider provider) => _provider = provider;

    public Task<TResponse> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(request.GetType(), typeof(TResponse));

        var handler = _provider.GetService(handlerType)
            ?? throw new InvalidOperationException($"No handler registered for {request.GetType().Name}.");

        var handle = handlerType.GetMethod("HandleAsync")
            ?? throw new MissingMethodException(handlerType.FullName, "HandleAsync");

        return (Task<TResponse>)handle.Invoke(handler, [request, cancellationToken])!;
    }
}
