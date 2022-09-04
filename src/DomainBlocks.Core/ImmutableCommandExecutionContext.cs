using System;
using System.Collections.Generic;

namespace DomainBlocks.Core;

public class ImmutableCommandExecutionContext<TAggregate> : ICommandExecutionContext<TAggregate>
{
    private readonly IImmutableAggregateType<TAggregate> _aggregateType;
    private readonly List<object> _raisedEvents = new();

    public ImmutableCommandExecutionContext(TAggregate state, IImmutableAggregateType<TAggregate> aggregateType)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        _aggregateType = aggregateType ?? throw new ArgumentNullException(nameof(aggregateType));
    }

    public TAggregate State { get; private set; }
    public IEnumerable<object> RaisedEvents => _raisedEvents;

    public TCommandResult ExecuteCommand<TCommandResult>(Func<TAggregate, TCommandResult> commandExecutor)
    {
        var commandResult = commandExecutor(State);
        var commandReturnType = _aggregateType.GetCommandReturnType<TCommandResult>();
        (var events, State) = commandReturnType.SelectEventsAndUpdateState(commandResult, State);
        _raisedEvents.AddRange(events);

        return commandResult;
    }

    public void ExecuteCommand(Action<TAggregate> commandExecutor)
    {
        throw new InvalidOperationException("Cannot invoke a void command on an immutable aggregate type.");
    }
}