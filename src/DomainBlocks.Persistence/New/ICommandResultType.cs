using System;
using System.Collections.Generic;

namespace DomainBlocks.Persistence.New;

public interface ICommandResultType
{
    public Type ClrType { get; }
}

public interface ICommandResultType<TAggregate, in TCommandResult> : ICommandResultType
{
    public (TAggregate, IEnumerable<object>) GetUpdatedStateAndEvents(
        TCommandResult commandResult, TAggregate aggregate);
}