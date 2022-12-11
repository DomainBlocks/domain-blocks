using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections.Internal;

internal sealed class InterceptingEventHandlerInvoker<TState> : IEventHandlerInvoker<TState>
{
    private readonly IEventHandlerInvoker<TState> _invoker;
    private readonly IEventHandlerInterceptor<TState> _interceptor;

    public InterceptingEventHandlerInvoker(
        IEventHandlerInvoker<TState> invoker, IEventHandlerInterceptor<TState> interceptor)
    {
        _invoker = invoker;
        _interceptor = interceptor;
    }

    public Task Invoke(IEventRecord eventRecord, TState state, CancellationToken cancellationToken)
    {
        return _interceptor.Handle(
            eventRecord,
            state,
            ct => _invoker.Invoke(eventRecord, state, ct),
            cancellationToken);
    }
}