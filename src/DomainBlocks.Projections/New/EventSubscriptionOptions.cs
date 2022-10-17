namespace DomainBlocks.Projections.New;

public class EventSubscriptionOptions
{
    public EventSubscriptionOptions(ProjectionRegistry projections, IEventDispatcher eventDispatcher)
    {
        Projections = projections;
        EventDispatcher = eventDispatcher;
    }

    public ProjectionRegistry Projections { get; }
    public IEventDispatcher EventDispatcher { get; }
}