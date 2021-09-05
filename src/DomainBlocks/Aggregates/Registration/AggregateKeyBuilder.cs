using System;

namespace DomainBlocks.Aggregates.Registration
{
    public class AggregateKeyBuilder<TAggregate, TCommandBase, TEventBase>
    {
        private readonly AggregateRegistryBuilder<TCommandBase, TEventBase> _aggregateRegistryBuilder;

        public AggregateKeyBuilder(AggregateRegistryBuilder<TCommandBase, TEventBase> aggregateRegistryBuilder)
        {
            _aggregateRegistryBuilder = aggregateRegistryBuilder;
        }

        public AggregateKeyBuilder<TAggregate, TCommandBase, TEventBase> PersistenceKey(Func<string, string> getPersistenceKey)
        {
            _aggregateRegistryBuilder.RegisterAggregateKey<TAggregate>(getPersistenceKey);
            return this;
        }

        public AggregateKeyBuilder<TAggregate, TCommandBase, TEventBase> SnapshotKey(Func<string, string> getSnapshotKey)
        {
            _aggregateRegistryBuilder.RegisterAggregateSnapshotKey<TAggregate>(getSnapshotKey);
            return this;
        }
    }
}