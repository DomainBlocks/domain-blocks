using System.Collections.Generic;

namespace DomainLib.Aggregates
{
    /// <summary>
    /// Represent the state mutations that occur as a result of an aggregate root executing a command.
    /// </summary>
    public interface ICommandResult<out TAggregateRoot, out TDomainEventBase>
    {
        /// <summary>
        /// The updated aggregate root, which is the result of applying the events produced by executing a command.
        /// </summary>
        TAggregateRoot NewState { get; }

        /// <summary>
        /// The events that were applied to the aggregate root as part of executing a command.
        /// </summary>
        IReadOnlyList<TDomainEventBase> AppliedEvents { get; }
    }
}