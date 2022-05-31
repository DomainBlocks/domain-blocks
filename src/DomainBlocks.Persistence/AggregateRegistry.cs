using DomainBlocks.Aggregates;

namespace DomainBlocks.Persistence;

public sealed class AggregateRegistry<TEventBase>
{
    internal AggregateRegistry(EventRegistry<TEventBase> eventRegistry, AggregateMetadataMap aggregateMetadataMap)
    {
        EventRoutes = eventRegistry.EventRoutes;
        EventNameMap = eventRegistry.EventNameMap;
        AggregateMetadataMap = aggregateMetadataMap;
    }
    
    public EventRoutes<TEventBase> EventRoutes { get; }
    public IEventNameMap EventNameMap { get; }
    public AggregateMetadataMap AggregateMetadataMap { get; }
}