using System;

namespace DomainBlocks.Aggregates.Registration
{
    public sealed class AggregateRegistrationBuilder<TAggregate, TCommandBase, TEventBase>
    {
        private readonly AggregateRegistryBuilder<TCommandBase, TEventBase> _aggregateRegistryBuilder;

        public AggregateRegistrationBuilder(AggregateRegistryBuilder<TCommandBase, TEventBase> aggregateRegistryBuilder)
        {
            _aggregateRegistryBuilder = aggregateRegistryBuilder;
        }

        public AggregateKeyBuilder<TAggregate, TCommandBase, TEventBase> Id(Func<TAggregate, string> getPersistenceId)
        {
            _aggregateRegistryBuilder.RegisterAggregateIdFunc(getPersistenceId);
            return new AggregateKeyBuilder<TAggregate, TCommandBase, TEventBase>(_aggregateRegistryBuilder);
        }

        public CommandRegistrationBuilder<TAggregate, TCommandBase, TCommand, TEventBase> Command<TCommand>() where TCommand : TCommandBase
        {
            return new(_aggregateRegistryBuilder);
        }

        public EventRegistrationBuilder<TAggregate, TCommandBase, TEventBase, TEvent> Event<TEvent>() where TEvent : TEventBase
        {
            return new(_aggregateRegistryBuilder);
        }


    }
}