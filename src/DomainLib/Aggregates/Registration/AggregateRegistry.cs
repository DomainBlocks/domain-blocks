namespace DomainLib.Aggregates.Registration
{
    public sealed class AggregateRegistry<TCommandBase, TEventBase>
    {
        internal AggregateRegistry(
            CommandRegistrations<TCommandBase, TEventBase> commandRegistrations,
            EventRoutes<TEventBase> eventRoutes,
            ImmutableEventRoutes<TEventBase> immutableEventRoutes,
            IEventNameMap eventNameMap,
            AggregateMetadataMap aggregateMetadataMap)
        {
            EventDispatcher = new EventDispatcher<TEventBase>(eventRoutes, immutableEventRoutes);
            CommandDispatcher = new CommandDispatcher<TCommandBase, TEventBase>(commandRegistrations, EventDispatcher);
            EventNameMap = eventNameMap;
            AggregateMetadataMap = aggregateMetadataMap;
        }
        
        public CommandDispatcher<TCommandBase, TEventBase> CommandDispatcher { get; }
        public EventDispatcher<TEventBase> EventDispatcher { get; }
        public IEventNameMap EventNameMap { get; }
        public AggregateMetadataMap AggregateMetadataMap { get; }
    }
}