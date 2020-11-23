using System.Collections.Generic;

namespace DomainLib.Aggregates
{
    /// <summary>
    /// Executes a command on a mutable aggregate root, and returns one or more domain events that represent what has
    /// occurred.
    /// </summary>
    public delegate IEnumerable<TEvent> ExecuteCommand<in TAggregate, in TCommand, out TEvent>(
        TAggregate aggregate, TCommand command);
}