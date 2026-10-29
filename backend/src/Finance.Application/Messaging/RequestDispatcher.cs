namespace Finance.Application.Messaging;

public interface IRequest<TResponse> { }
public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, CancellationToken ct);
}
public delegate Task<TResponse> RequestNext<TResponse>();
public interface IRequestBehavior<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, RequestNext<TResponse> next, CancellationToken ct);
}

// Explicit type arguments keep dispatch statically typed, without reflection or a package dependency.
public sealed class RequestDispatcher<TRequest, TResponse>(IRequestHandler<TRequest, TResponse> handler,
    IEnumerable<IRequestBehavior<TRequest, TResponse>> behaviors) where TRequest : IRequest<TResponse>
{
    public Task<TResponse> SendAsync(TRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        RequestNext<TResponse> next = () => handler.HandleAsync(request, ct);
        foreach (var behavior in behaviors.Reverse())
        {
            var continuation = next;
            next = () => behavior.HandleAsync(request, continuation, ct);
        }
        return next();
    }
}
