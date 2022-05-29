using DomainBlocks.Aggregates;

namespace DomainBlocks.Persistence;

public sealed class AggregateRegistry<TEventBase>
{
    internal AggregateRegistry(
        IAggregateEventRouter<TEventBase> eventRouter,
        IEventNameMap eventNameMap,
        AggregateMetadataMap aggregateMetadataMap)
    {
        EventRouter = eventRouter;
        EventNameMap = eventNameMap;
        AggregateMetadataMap = aggregateMetadataMap;
    }
    
    public IAggregateEventRouter<TEventBase> EventRouter { get; }
    public IEventNameMap EventNameMap { get; }
    public AggregateMetadataMap AggregateMetadataMap { get; }
}