using System;
using System.Collections.Generic;

namespace DomainBlocks.Aggregates
{
    /// <summary>
    /// Executes a command on an immutable aggregate root, and returns one or more domain events that represent what has
    /// occurred.
    /// </summary>
    public delegate IEnumerable<TEvent> ImmutableExecuteCommand<in TAggregate, in TCommand, out TEvent>(
        Func<TAggregate> getAggregate, TCommand command);
}