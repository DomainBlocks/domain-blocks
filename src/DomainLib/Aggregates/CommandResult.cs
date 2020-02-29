using System.Collections.Generic;
using System.Collections.Immutable;

namespace DomainLib.Aggregates
{
    public class CommandResult<TAggregateRoot, TDomainEventBase> : ICommandResult<TAggregateRoot, TDomainEventBase>
    {
        private readonly ImmutableList<TDomainEventBase> _appliedEvents;

        public CommandResult(TAggregateRoot aggregateRoot) : this(aggregateRoot, ImmutableList<TDomainEventBase>.Empty)
        {
        }

        private CommandResult(TAggregateRoot aggregateRoot, ImmutableList<TDomainEventBase> appliedEvents)
        {
            NewState = aggregateRoot;
            _appliedEvents = appliedEvents;
        }
        
        public TAggregateRoot NewState { get; }
        public IReadOnlyList<TDomainEventBase> AppliedEvents => _appliedEvents;

        public CommandResult<TAggregateRoot, TDomainEventBase> WithNewState(
            TAggregateRoot aggregateRoot, TDomainEventBase @appliedEvent)
        {
            return new CommandResult<TAggregateRoot, TDomainEventBase>(aggregateRoot, _appliedEvents.Add(appliedEvent));
        }
    }
}