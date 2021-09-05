namespace DomainBlocks.Aggregates.Registration
{
    public sealed class CommandRegistrationBuilder<TAggregate, TCommandBase, TCommand, TEventBase>
        where TCommand : TCommandBase
    {
        private readonly AggregateRegistryBuilder<TCommandBase, TEventBase> _aggregateRegistryBuilder;

        public CommandRegistrationBuilder(AggregateRegistryBuilder<TCommandBase, TEventBase> aggregateRegistryBuilder)
        {
            _aggregateRegistryBuilder = aggregateRegistryBuilder;
        }
        
        public CommandRegistrationBuilder<TAggregate, TCommandBase, TCommand, TEventBase> RoutesTo(
            ExecuteCommand<TAggregate, TCommand, TEventBase> executeCommand)
        {
            _aggregateRegistryBuilder.RegisterCommandRoute(executeCommand);
            return this;
        }

        public CommandRegistrationBuilder<TAggregate, TCommandBase, TCommand, TEventBase> RoutesTo(
            ImmutableExecuteCommand<TAggregate, TCommand, TEventBase> executeCommand)
        {
            _aggregateRegistryBuilder.RegisterCommandRoute(executeCommand);
            return this;
        }
    }
}