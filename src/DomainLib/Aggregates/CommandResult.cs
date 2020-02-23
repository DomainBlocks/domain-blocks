using System.Collections.Generic;
using System.Collections.Immutable;

namespace DomainLib.Aggregates
{
    /// <summary>
    /// Represents the result of an aggregate root handling a command.
    /// TODO: Could split out the behaviour to do with executing the command into a separate class, then this class
    /// could just be a POCO.
    /// </summary>
    /// <typeparam name="TAggregateRoot">The type of the aggregate root that handles the command.</typeparam>
    /// <typeparam name="TDomainEventBase">The base type of all events that are applied.</typeparam>
    public class CommandResult<TAggregateRoot, TDomainEventBase>
    {
        private readonly EventRouter<TAggregateRoot, TDomainEventBase> _routes =
            new EventRouter<TAggregateRoot, TDomainEventBase>();
        
        private readonly ImmutableList<TDomainEventBase> _appliedEvents = ImmutableList<TDomainEventBase>.Empty;

        public CommandResult(TAggregateRoot aggregateRoot)
        {
            AggregateRoot = aggregateRoot;
        }

        private CommandResult(
            TAggregateRoot aggregateRoot,
            EventRouter<TAggregateRoot, TDomainEventBase> routes,
            ImmutableList<TDomainEventBase> appliedEvents)
        {
            AggregateRoot = aggregateRoot;
            _routes = routes;
            _appliedEvents = appliedEvents;
        }

        /// <summary>
        /// The updated aggregate root, which is the result of applying the events of a command.
        /// </summary>
        public TAggregateRoot AggregateRoot { get; }

        /// <summary>
        /// The events that were applied to the aggregate root in a command.
        /// </summary>
        public IReadOnlyList<TDomainEventBase> AppliedEvents => _appliedEvents;
        
        /// <summary>
        /// Sets an EventRouter for routing events to apply methods on the aggregate root.
        /// </summary>
        public CommandResult<TAggregateRoot, TDomainEventBase> WithEventRouter(
            EventRouter<TAggregateRoot, TDomainEventBase> routes)
        {
            return new CommandResult<TAggregateRoot, TDomainEventBase>(AggregateRoot, routes, _appliedEvents);
        }

        /// <summary>
        /// Applies an event to the aggregate root.
        /// </summary>
        public CommandResult<TAggregateRoot, TDomainEventBase> ApplyEvent<TDomainEvent>(TDomainEvent @event)
            where TDomainEvent : TDomainEventBase
        {
            var result = _routes.Apply(AggregateRoot, @event);
            return new CommandResult<TAggregateRoot, TDomainEventBase>(result, _routes, _appliedEvents.Add(@event));
        }
    }
}
