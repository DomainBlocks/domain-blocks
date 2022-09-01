using System.Collections.Generic;

namespace DomainBlocks.Persistence.New;

public interface ICommandResultStrategy<TAggregate, TEventBase, in TCommandResult>
{
    public (TAggregate, IEnumerable<TEventBase>) GetUpdatedStateAndEvents(
        TCommandResult commandResult, TAggregate aggregate);
}