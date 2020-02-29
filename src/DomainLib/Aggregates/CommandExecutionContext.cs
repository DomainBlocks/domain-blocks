namespace DomainLib.Aggregates
{
    public static class CommandExecutionContext
    {
        public static CommandExecutionContext<TAggregateRoot, TDomainEventBase>
            Create<TAggregateRoot, TDomainEventBase>(
                TAggregateRoot aggregateRoot,
                ApplyEventRouter<TAggregateRoot, TDomainEventBase> applyEventRouter)
        {
            var commandResult = new CommandResult<TAggregateRoot, TDomainEventBase>(aggregateRoot);
            return new CommandExecutionContext<TAggregateRoot, TDomainEventBase>(commandResult, applyEventRouter);
        }
    }
    
    /// <summary>
    /// Applies state mutations to an aggregate root by routing the events that occur as part of executing a command
    /// to their appropriate "apply event" methods.
    /// </summary>
    public class CommandExecutionContext<TAggregateRoot, TDomainEventBase>
    {
        private readonly ApplyEventRouter<TAggregateRoot, TDomainEventBase> _applyEventRouter;

        public CommandExecutionContext(
            CommandResult<TAggregateRoot, TDomainEventBase> commandResult,
            ApplyEventRouter<TAggregateRoot, TDomainEventBase> applyEventRouter)
        {
            Result = commandResult;
            _applyEventRouter = applyEventRouter;
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
            var newState = _applyEventRouter.Route(Result.NewState, @event);
            var newResult = Result.WithNewState(newState, @event);
            return new CommandExecutionContext<TAggregateRoot, TDomainEventBase>(newResult, _applyEventRouter);
        }
    }
}