namespace DomainLib.Aggregates.Registration
{
    public sealed class AggregateRegistry<TCommandBase, TEventBase>
    {
        public CommandDispatcher<TCommandBase, TEventBase> CommandDispatcher { get; }
        public EventDispatcher<TEventBase> EventDispatcher { get; }
        public IEventNameMap EventNameMap { get; }
        public AggregateMetadataMap AggregateMetadataMap { get; }

        internal AggregateRegistry(CommandRegistrations<TCommandBase, TEventBase> commandRegistrations,
                                 EventRoutes<TEventBase> eventRoutes,
                                 IEventNameMap eventNameMap,
                                 AggregateMetadataMap aggregateMetadataMap)
        {
            EventDispatcher = new EventDispatcher<TEventBase>(eventRoutes);
            CommandDispatcher = new CommandDispatcher<TCommandBase, TEventBase>(commandRegistrations, EventDispatcher);
            EventNameMap = eventNameMap;
            AggregateMetadataMap = aggregateMetadataMap;
        }
    }
}