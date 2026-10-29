using Finance.Application.Abstractions;
using Finance.Application.Messaging;

namespace Finance.UnitTests.Application;

public sealed class RequestDispatcherTests
{
    private sealed record Request : IRequest<int>, IAuthenticatedRequest;
    private sealed class Handler(List<string> log) : IRequestHandler<Request, int>
    {
        public Task<int> HandleAsync(Request request, CancellationToken ct) { log.Add("handler"); ct.ThrowIfCancellationRequested(); return Task.FromResult(42); }
    }
    private sealed class Behavior(string name, List<string> log) : IRequestBehavior<Request, int>
    {
        public async Task<int> HandleAsync(Request request, RequestNext<int> next, CancellationToken ct)
        { log.Add(name + ":before"); var result = await next(); log.Add(name + ":after"); return result; }
    }
    private sealed class User(Guid? id) : ICurrentUser { public Guid? UserId => id; }
    [Fact]
    public async Task BehaviorsWrapTheHandlerInRegistrationOrder()
    {
        var log = new List<string>();
        var dispatcher = new RequestDispatcher<Request, int>(new Handler(log), [new Behavior("first", log), new Behavior("second", log)]);
        Assert.Equal(42, await dispatcher.SendAsync(new(), CancellationToken.None));
        Assert.Equal(new[] { "first:before", "second:before", "handler", "second:after", "first:after" }, log);
    }
    [Fact]
    public async Task AnonymousOrCancelledRequestsNeverReachTheHandler()
    {
        var log = new List<string>();
        var dispatcher = new RequestDispatcher<Request, int>(new Handler(log), [new AuthenticatedRequestBehavior<Request, int>(new User(null))]);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => dispatcher.SendAsync(new(), CancellationToken.None));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatcher.SendAsync(new(), cts.Token));
        Assert.Empty(log);
    }
}
