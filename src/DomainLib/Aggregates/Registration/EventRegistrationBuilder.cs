namespace DomainLib.Aggregates.Registration
{
    public sealed class EventRegistrationBuilder<TAggregate, TCommandBase, TEventBase, TEvent> where TEvent : TEventBase
    {
        private readonly AggregateRegistryBuilder<TCommandBase, TEventBase> _aggregateRegistryBuilder;

        public EventRegistrationBuilder(AggregateRegistryBuilder<TCommandBase, TEventBase> aggregateRegistryBuilder)
        {
            _aggregateRegistryBuilder = aggregateRegistryBuilder;
        }
        
        public EventRegistrationBuilder<TAggregate, TCommandBase, TEventBase, TEvent> RoutesTo(
            ApplyEvent<TAggregate, TEvent> applyEvent)
        {
            _aggregateRegistryBuilder.RegisterEventRoute(applyEvent);
            return this;
        }

        public EventRegistrationBuilder<TAggregate, TCommandBase, TEventBase, TEvent> RoutesTo(
            ImmutableApplyEvent<TAggregate, TEvent> applyEvent)
        {
            _aggregateRegistryBuilder.RegisterEventRoute(applyEvent);
            return this;
        }

        public EventRegistrationBuilder<TAggregate, TCommandBase, TEventBase, TEvent> HasName(string name)
        {
            _aggregateRegistryBuilder.RegisterEventName<TEvent>(name);
            return this;
        }
    }
}