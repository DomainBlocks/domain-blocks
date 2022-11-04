using System;
using System.Collections.Generic;

namespace DomainBlocks.Core;

public interface ICommandExecutionContext<out TAggregate>
{
    TAggregate State { get; }
    IReadOnlyCollection<object> RaisedEvents { get; }

    TCommandResult ExecuteCommand<TCommandResult>(Func<TAggregate, TCommandResult> commandExecutor);
    void ExecuteCommand(Action<TAggregate> commandExecutor);
}