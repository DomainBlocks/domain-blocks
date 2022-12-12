using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections.New.Internal;

internal static class EventHandlerInvoker
{
    public static EventHandlerInvoker<TState> Create<TEvent, TState>(EventHandler<TEvent, TState> eventHandler)
    {
        return new EventHandlerInvoker<TState>((e, s, ct) => eventHandler(e.Cast<TEvent>(), s, ct));
    }
}

internal sealed class EventHandlerInvoker<TState> : IEventHandlerInvoker<TState>
{
    private readonly EventHandler<object, TState> _eventHandler;

    public EventHandlerInvoker(EventHandler<object, TState> eventHandler)
    {
        _eventHandler = eventHandler;
    }

    public Task Invoke(IEventRecord eventRecord, TState state, CancellationToken cancellationToken)
    {
        return _eventHandler((IEventRecord<object>)eventRecord, state, cancellationToken);
    }
}