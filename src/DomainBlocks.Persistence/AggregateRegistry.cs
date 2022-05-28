using DomainBlocks.Aggregates;

namespace DomainBlocks.Persistence;

public sealed class AggregateRegistry<TEventBase>
{
    internal AggregateRegistry(
        EventRoutes<TEventBase> eventRoutes, IEventNameMap eventNameMap, AggregateMetadataMap aggregateMetadataMap)
    {
        EventDispatcher = new EventDispatcher<TEventBase>(eventRoutes);
        EventNameMap = eventNameMap;
        AggregateMetadataMap = aggregateMetadataMap;
    }
    
    public EventDispatcher<TEventBase> EventDispatcher { get; }
    public IEventNameMap EventNameMap { get; }
    public AggregateMetadataMap AggregateMetadataMap { get; }
}