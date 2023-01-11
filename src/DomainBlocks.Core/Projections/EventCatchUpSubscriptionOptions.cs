namespace DomainBlocks.Core.Projections;

public class EventCatchUpSubscriptionOptions
{
    private Func<ProjectionRegistry, IEventDispatcher>? _eventDispatcherFactory;

    public EventCatchUpSubscriptionOptions()
    {
    }

    private EventCatchUpSubscriptionOptions(EventCatchUpSubscriptionOptions copyFrom)
    {
        _eventDispatcherFactory = copyFrom._eventDispatcherFactory;
    }

    public EventCatchUpSubscriptionOptions WithEventDispatcherFactory(
        Func<ProjectionRegistry, IEventDispatcher> eventDispatcherFactory)
    {
        return new EventCatchUpSubscriptionOptions(this) { _eventDispatcherFactory = eventDispatcherFactory };
    }

    public IEventDispatcher CreateEventDispatcher(ProjectionRegistry projectionRegistry)
    {
        if (_eventDispatcherFactory == null)
        {
            throw new InvalidOperationException("No event dispatcher factory specified");
        }

        return _eventDispatcherFactory(projectionRegistry);
    }
}