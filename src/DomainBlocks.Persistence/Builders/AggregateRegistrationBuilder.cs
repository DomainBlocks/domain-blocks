using System;
using DomainBlocks.Aggregates;
using DomainBlocks.Aggregates.Builders;

namespace DomainBlocks.Persistence.Builders;

public sealed class AggregateRegistrationBuilder<TAggregate, TEventBase>
{
    private readonly AggregateRegistryBuilder<TEventBase> _aggregateRegistryBuilder;

    public AggregateRegistrationBuilder(AggregateRegistryBuilder<TEventBase> aggregateRegistryBuilder)
    {
        _aggregateRegistryBuilder = aggregateRegistryBuilder;
    }

    public AggregateRegistrationBuilder<TAggregate, TEventBase> InitialState(
        Func<AggregateEventRouter<TEventBase>, TAggregate> initialStateFactory)
    {
        _aggregateRegistryBuilder.RegisterInitialStateFunc(initialStateFactory);
        return this;
    }

    public AggregateRegistrationBuilder<TAggregate, TEventBase> Id(Func<TAggregate, string> idSelector)
    {
        _aggregateRegistryBuilder.RegisterAggregateIdFunc(idSelector);
        return new AggregateRegistrationBuilder<TAggregate, TEventBase>(_aggregateRegistryBuilder);
    }

    public AggregateRegistrationBuilder<TAggregate, TEventBase> PersistenceKey(Func<string, string> idToKeySelector)
    {
        _aggregateRegistryBuilder.RegisterAggregateKey<TAggregate>(idToKeySelector);
        return this;
    }

    public AggregateRegistrationBuilder<TAggregate, TEventBase> SnapshotKey(
        Func<string, string> idToSnapshotKeySelector)
    {
        _aggregateRegistryBuilder.RegisterAggregateSnapshotKey<TAggregate>(idToSnapshotKeySelector);
        return this;
    }

    public AggregateRegistrationBuilder<TAggregate, TEventBase> RegisterEvents(
        Action<EventRegistryBuilder<TAggregate, TEventBase>> builderAction)
    {
        _aggregateRegistryBuilder.Events.For(builderAction);
        return this;
    }
}