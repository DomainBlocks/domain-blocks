using DomainBlocks.Aggregates;

namespace DomainBlocks.Persistence;

public sealed class AggregateRegistry<TEventBase>
{
    internal AggregateRegistry(EventRegistry<TEventBase> eventRegistry, AggregateMetadataMap aggregateMetadataMap)
    {
        EventApplierMap = eventRegistry.EventApplierMap;
        EventNameMap = eventRegistry.EventNameMap;
        AggregateMetadataMap = aggregateMetadataMap;
    }
    
    public EventApplierMap<TEventBase> EventApplierMap { get; }
    public IEventNameMap EventNameMap { get; }
    internal AggregateMetadataMap AggregateMetadataMap { get; }
}