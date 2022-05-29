using System;
using DomainBlocks.Aggregates;
using DomainBlocks.Aggregates.Builders;

namespace DomainBlocks.Persistence.Builders;

public static class AggregateRegistryBuilder
{
    public static AggregateRegistryBuilder<TEventBase> Create<TEventBase>() => new();
    public static AggregateRegistryBuilder<object> Create() => new();
}

public sealed class AggregateRegistryBuilder<TEventBase>
{
    private readonly AggregateMetadataMap _aggregateMetadataMap = new();
    
    public EventRegistryBuilder<TEventBase> Events { get; } = new();

    public AggregateRegistry<TEventBase> Build()
    {
        var eventRegistry = Events.Build();
        return new(eventRegistry.EventRoutes, eventRegistry.EventNameMap, _aggregateMetadataMap);
    }

    public AggregateRegistryBuilder<TEventBase> Register<TAggregate>(
        Action<AggregateRegistrationBuilder<TAggregate, TEventBase>> builderAction)
    {
        if (builderAction == null) throw new ArgumentNullException(nameof(builderAction));
        builderAction(new AggregateRegistrationBuilder<TAggregate, TEventBase>(this));
        return this;
    }

    internal void RegisterInitialStateFunc<TAggregate>(
        Func<IEventDispatcher<TEventBase>, TAggregate> initialStateFactory)
    {
        var aggregateMetadata = GetOrAddAggregateMetadata<TAggregate>();
        aggregateMetadata.InitialStateFactory = x => initialStateFactory((IEventDispatcher<TEventBase>)x);
    }

    internal void RegisterAggregateIdFunc<TAggregate>(Func<TAggregate, string> idSelector)
    {
        var aggregateMetadata = GetOrAddAggregateMetadata<TAggregate>();
        aggregateMetadata.IdSelector = x => idSelector((TAggregate)x);
    }

    internal void RegisterAggregateKey<TAggregate>(Func<string, string> idToKeySelector)
    {
        var aggregateMetadata = GetOrAddAggregateMetadata<TAggregate>();
        aggregateMetadata.IdToKeySelector = idToKeySelector;
    }

    internal void RegisterAggregateSnapshotKey<TAggregate>(Func<string, string> snapshotKeySelector)
    {
        var aggregateMetadata = GetOrAddAggregateMetadata<TAggregate>();
        aggregateMetadata.IdToSnapshotKeySelector = snapshotKeySelector;
    }

    private AggregateMetadata GetOrAddAggregateMetadata<TAggregate>()
    {
        var aggregateType = typeof(TAggregate);
        if (!_aggregateMetadataMap.TryGetValue(aggregateType, out var aggregateMetadata))
        {
            aggregateMetadata = new AggregateMetadata();
            _aggregateMetadataMap.Add(aggregateType, aggregateMetadata);
        }

        return aggregateMetadata;
    }
}