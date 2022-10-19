namespace DomainBlocks.Projections.New;

public class EventCatchUpSubscriptionOptions
{
    public EventCatchUpSubscriptionOptions(IEventDispatcher eventDispatcher)
    {
        EventDispatcher = eventDispatcher;
    }
    
    public IEventDispatcher EventDispatcher { get; }
}