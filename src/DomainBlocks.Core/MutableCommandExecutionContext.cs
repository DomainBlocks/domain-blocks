using System;
using System.Collections.Generic;

namespace DomainBlocks.Core;

public class MutableCommandExecutionContext<TAggregate> : ICommandExecutionContext<TAggregate>
{
    private readonly IMutableAggregateType<TAggregate> _aggregateType;
    private readonly List<object> _raisedEvents = new();

    public MutableCommandExecutionContext(TAggregate state, IMutableAggregateType<TAggregate> aggregateType)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        _aggregateType = aggregateType ?? throw new ArgumentNullException(nameof(aggregateType));
    }

    public TAggregate State { get; }
    public IEnumerable<object> RaisedEvents => _raisedEvents;

    public TCommandResult ExecuteCommand<TCommandResult>(Func<TAggregate, TCommandResult> commandExecutor)
    {
        var commandResult = commandExecutor(State);

        if (_aggregateType.CanSelectRaisedEventsFromAggregate)
        {
            var raisedEvents = _aggregateType.SelectRaisedEvents(State);
            _raisedEvents.Clear();
            _raisedEvents.AddRange(raisedEvents);
        }
        else
        {
            var commandReturnType = _aggregateType.GetCommandReturnType<TCommandResult>();
            var raisedEvents = commandReturnType.SelectEventsAndUpdateState(commandResult, State);
            _raisedEvents.AddRange(raisedEvents);
        }

        return commandResult;
    }

    public void ExecuteCommand(Action<TAggregate> commandExecutor)
    {
        commandExecutor(State);
        
        if (!_aggregateType.CanSelectRaisedEventsFromAggregate)
        {
            throw new InvalidOperationException(
                "Cannot execute void command. Aggregate has no raised events selector specified.");
        }

        var raisedEvents = _aggregateType.SelectRaisedEvents(State);
        _raisedEvents.Clear();
        _raisedEvents.AddRange(raisedEvents);
    }
}