using System;
using DomainBlocks.Aggregates;

namespace DomainBlocks.Persistence.Builders;

public static class AggregateRegistryBuilder
{
    public static AggregateRegistryBuilder<TEventBase> Create<TEventBase>() => new();

    public static AggregateRegistryBuilder<object> Create() => Create<object>();
}

public sealed class AggregateRegistryBuilder<TEventBase>
{
    private readonly EventRoutes<TEventBase> _eventRoutes = new();
    private readonly EventNameMap _eventNameMap = new();
    private readonly AggregateMetadataMap _aggregateMetadataMap = new();

    public AggregateRegistry<TEventBase> Build() => new(_eventRoutes, _eventNameMap, _aggregateMetadataMap);
        
    public void Register<TAggregate>(Action<AggregateRegistrationBuilder<TAggregate, TEventBase>> builderAction)
    {
        if (builderAction == null) throw new ArgumentNullException(nameof(builderAction));
        builderAction(new AggregateRegistrationBuilder<TAggregate, TEventBase>(this));
    }
        
    internal void RegisterInitialStateFunc<TAggregate>(Func<TAggregate> getState)
    {
        var aggregateMetadata = GetOrAddAggregateMetadata<TAggregate>();
        aggregateMetadata.GetInitialState = () => getState();
    }

    internal void RegisterAggregateIdFunc<TAggregate>(Func<TAggregate, string> getId)
    {
        var aggregateMetadata = GetOrAddAggregateMetadata<TAggregate>();
        aggregateMetadata.GetIdentifier = o => getId((TAggregate) o);
    }

    internal void RegisterAggregateKey<TAggregate>(Func<string, string> getPersistenceKey)
    {
        var aggregateMetadata = GetOrAddAggregateMetadata<TAggregate>();
        aggregateMetadata.GetKeyFromIdentifier = getPersistenceKey;
    }

    internal void RegisterAggregateSnapshotKey<TAggregate>(Func<string, string> getSnapshotKey)
    {
        var aggregateMetadata = GetOrAddAggregateMetadata<TAggregate>();
        aggregateMetadata.GetSnapshotKeyFromIdentifier = getSnapshotKey;
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