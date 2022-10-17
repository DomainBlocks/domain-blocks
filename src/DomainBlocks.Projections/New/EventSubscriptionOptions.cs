namespace DomainBlocks.Projections.New;

public class EventSubscriptionOptions
{
    public EventSubscriptionOptions(IEventDispatcher eventDispatcher)
    {
        EventDispatcher = eventDispatcher;
    }
    
    public IEventDispatcher EventDispatcher { get; }
}