using DomainBlocks.V1.Abstractions.Subscriptions.Messages;

namespace DomainBlocks.V1.Subscriptions.Messages;

public class EventMapped : ISubscriptionMessage
{
    public EventMapped(EventHandlerContext eventHandlerContext)
    {
        EventHandlerContext = eventHandlerContext;
    }

    public EventHandlerContext EventHandlerContext { get; }
}