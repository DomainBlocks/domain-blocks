using System;

namespace DomainBlocks.Persistence.Builders;

public class AggregateKeyBuilder<TAggregate, TEventBase>
{
    private readonly AggregateRegistryBuilder<TEventBase> _aggregateRegistryBuilder;

    public AggregateKeyBuilder(AggregateRegistryBuilder<TEventBase> aggregateRegistryBuilder)
    {
        _aggregateRegistryBuilder = aggregateRegistryBuilder;
    }

    public AggregateKeyBuilder<TAggregate, TEventBase> PersistenceKey(Func<string, string> getPersistenceKey)
    {
        _aggregateRegistryBuilder.RegisterAggregateKey<TAggregate>(getPersistenceKey);
        return this;
    }

    public AggregateKeyBuilder<TAggregate, TEventBase> SnapshotKey(Func<string, string> getSnapshotKey)
    {
        _aggregateRegistryBuilder.RegisterAggregateSnapshotKey<TAggregate>(getSnapshotKey);
        return this;
    }
}