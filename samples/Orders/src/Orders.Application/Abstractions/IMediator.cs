namespace Orders.Application.Abstractions;

/// <summary>
/// A deliberately tiny local mediator abstraction. The sample ships no MediatR dependency:
/// contracts reference <c>IMediator</c> and <c>IRequestHandler&lt;*, *&gt;</c> by name, and these are the names.
/// </summary>
public interface IRequest<out TResponse>;

public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken = default);
}

public interface IMediator
{
    Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
}
