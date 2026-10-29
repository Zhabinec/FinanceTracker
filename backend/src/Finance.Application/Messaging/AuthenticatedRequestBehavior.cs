using Finance.Application.Abstractions;

namespace Finance.Application.Messaging;

public interface IAuthenticatedRequest { }
public sealed class AuthenticatedRequestBehavior<TRequest, TResponse>(ICurrentUser currentUser)
    : IRequestBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public Task<TResponse> HandleAsync(TRequest request, RequestNext<TResponse> next, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (request is IAuthenticatedRequest && currentUser.UserId is null) throw new UnauthorizedAccessException();
        return next();
    }
}
