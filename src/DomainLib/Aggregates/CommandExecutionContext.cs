namespace DomainLib.Aggregates
{
    public static class CommandExecutionContext
    {
        public static CommandExecutionContext<TAggregateRoot, TDomainEventBase>
            Create<TAggregateRoot, TDomainEventBase>(EventDispatcher<TDomainEventBase> eventDispatcher,
                                                     TAggregateRoot aggregateRoot)
        {
            var commandResult = new CommandResult<TAggregateRoot, TDomainEventBase>(aggregateRoot);
            return new CommandExecutionContext<TAggregateRoot, TDomainEventBase>(eventDispatcher, commandResult);
        }
    }

    /// <summary>
    /// Applies state mutations to an aggregate root by routing the events that occur as part of executing a command
    /// to their appropriate "apply event" methods.
    /// </summary>
    public sealed class CommandExecutionContext<TAggregateRoot, TDomainEventBase>
    {
        private readonly EventDispatcher<TDomainEventBase> _eventDispatcher;

        public CommandExecutionContext(EventDispatcher<TDomainEventBase> eventDispatcher,
                                       CommandResult<TAggregateRoot, TDomainEventBase> commandResult)
        {
            _eventDispatcher = eventDispatcher;
            Result = commandResult;
        }
        
        /// <summary>
        /// The result of executing a command on an aggregate root.
        /// </summary>
        public CommandResult<TAggregateRoot, TDomainEventBase> Result { get; }

        /// <summary>
        /// Applies an event to the aggregate root.
        /// </summary>
        public CommandExecutionContext<TAggregateRoot, TDomainEventBase> ApplyEvent<TDomainEvent>(TDomainEvent @event)
            where TDomainEvent : TDomainEventBase
        {
            var newState = _eventDispatcher.DispatchEvent(Result.NewState, @event);
            var newResult = Result.WithNewState(newState, @event);
            return new CommandExecutionContext<TAggregateRoot, TDomainEventBase>(_eventDispatcher, newResult);
        }
    }
}