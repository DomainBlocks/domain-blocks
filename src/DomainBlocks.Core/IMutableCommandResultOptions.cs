using System;
using System.Collections.Generic;

namespace DomainBlocks.Core;

public interface IMutableCommandResultOptions<TAggregate, in TCommandResult> : ICommandResultOptions
{
    IReadOnlyCollection<object> SelectEventsAndUpdateState(
        TCommandResult commandResult, TAggregate state, Action<TAggregate, object> eventApplier);
}