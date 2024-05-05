namespace DomainBlocks.V1.Abstractions.Subscriptions;

public class EventWrapper<TEvent> : IEventWrapper<TEvent> where TEvent : notnull
{
    public EventWrapper(TEvent @event, SubscriptionPosition position)
    {
        Event = @event;
        Position = position;
    }

    object IEventWrapper.Event => Event;
    public TEvent Event { get; }
    public SubscriptionPosition Position { get; }
}