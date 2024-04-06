using DomainBlocks.V1.Abstractions;

namespace DomainBlocks.V1.Subscriptions;

public class EventHandlerContext
{
    public EventHandlerContext(object @event, SubscriptionPosition position, CancellationToken cancellationToken)
    {
        Event = @event;
        Position = position;
        CancellationToken = cancellationToken;
    }

    public object Event { get; }
    public SubscriptionPosition Position { get; }
    public CancellationToken CancellationToken { get; }

    public EventHandlerContext<TEvent> Convert<TEvent>()
    {
        return new EventHandlerContext<TEvent>((TEvent)Event, Position, CancellationToken);
    }
}

public class EventHandlerContext<TEvent>
{
    public EventHandlerContext(TEvent @event, SubscriptionPosition position, CancellationToken cancellationToken)
    {
        Event = @event;
        Position = position;
        CancellationToken = cancellationToken;
    }

    public TEvent Event { get; }
    public SubscriptionPosition Position { get; }
    public CancellationToken CancellationToken { get; }
}