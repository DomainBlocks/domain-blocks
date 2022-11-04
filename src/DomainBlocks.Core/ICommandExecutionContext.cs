using System;
using System.Collections.Generic;

namespace DomainBlocks.Core;

public interface ICommandExecutionContext<out TAggregate>
{
    public TAggregate State { get; }
    public IReadOnlyCollection<object> RaisedEvents { get; }

    public TCommandResult ExecuteCommand<TCommandResult>(Func<TAggregate, TCommandResult> commandExecutor);
    public void ExecuteCommand(Action<TAggregate> commandExecutor);
}