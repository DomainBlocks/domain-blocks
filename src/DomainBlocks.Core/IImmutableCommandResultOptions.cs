using System;
using System.Collections.Generic;

namespace DomainBlocks.Core;

public interface IImmutableCommandResultOptions<TAggregate, TCommandResult> : ICommandResultOptions<TCommandResult>
{
    (IReadOnlyCollection<object>, TAggregate) SelectEventsAndUpdateState(
        TCommandResult commandResult, TAggregate state, Func<TAggregate, object, TAggregate> eventApplier);
}