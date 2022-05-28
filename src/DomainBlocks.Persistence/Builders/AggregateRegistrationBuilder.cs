using System;
using DomainBlocks.Aggregates.Builders;

namespace DomainBlocks.Persistence.Builders;

public sealed class AggregateRegistrationBuilder<TAggregate, TEventBase>
{
    private readonly AggregateRegistryBuilder<TEventBase> _aggregateRegistryBuilder;

    public AggregateRegistrationBuilder(AggregateRegistryBuilder<TEventBase> aggregateRegistryBuilder)
    {
        _aggregateRegistryBuilder = aggregateRegistryBuilder;
    }

    public AggregateRegistrationBuilder<TAggregate, TEventBase> InitialState(Func<TAggregate> getInitialState)
    {
        _aggregateRegistryBuilder.RegisterInitialStateFunc(getInitialState);
        return this;
    }

    public AggregateKeyBuilder<TAggregate, TEventBase> Id(Func<TAggregate, string> getPersistenceId)
    {
        _aggregateRegistryBuilder.RegisterAggregateIdFunc(getPersistenceId);
        return new AggregateKeyBuilder<TAggregate, TEventBase>(_aggregateRegistryBuilder);
    }

    public AggregateRegistrationBuilder<TAggregate, TEventBase> WithEvents(
        Action<EventRegistryBuilder<TAggregate, TEventBase>> builderAction)
    {
        var builder = EventRegistryBuilder.Create<TAggregate, TEventBase>();
        builderAction(builder);
        return this;
    }
}