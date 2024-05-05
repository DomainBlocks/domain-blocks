using DomainBlocks.V1.Abstractions.Subscriptions;
using MediatR;

namespace Shopping.ReadModel.Projections.MediatR;

public class EventNotification<TEvent> : IEventWrapper<TEvent>, INotification where TEvent : notnull
{
    public EventNotification(TEvent @event, SubscriptionPosition position)
    {
        Event = @event;
        Position = position;
    }

    object IEventWrapper.Event => Event;
    public TEvent Event { get; }
    public SubscriptionPosition Position { get; }
}