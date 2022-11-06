using System;
using System.Collections.Generic;

namespace DomainBlocks.Core;

public sealed class MutableCommandExecutionContext<TAggregate> : ICommandExecutionContext<TAggregate>
{
    private readonly IMutableAggregateOptions<TAggregate> _aggregateOptions;
    private readonly List<object> _raisedEvents = new();

    public MutableCommandExecutionContext(TAggregate state, IMutableAggregateOptions<TAggregate> aggregateOptions)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        _aggregateOptions = aggregateOptions ?? throw new ArgumentNullException(nameof(aggregateOptions));
    }

    public TAggregate State { get; }
    public IReadOnlyCollection<object> RaisedEvents => _raisedEvents.AsReadOnly();

    public TCommandResult ExecuteCommand<TCommandResult>(Func<TAggregate, TCommandResult> commandExecutor)
    {
        if (commandExecutor == null) throw new ArgumentNullException(nameof(commandExecutor));

        var commandResult = commandExecutor(State);

        if (_aggregateOptions.CanSelectRaisedEventsFromAggregate)
        {
            var raisedEvents = _aggregateOptions.SelectRaisedEvents(State);
            _raisedEvents.Clear();
            _raisedEvents.AddRange(raisedEvents);

            return commandResult;
        }

        {
            var commandResultOptions = _aggregateOptions.GetCommandResultOptions<TCommandResult>();
            var raisedEvents =
                commandResultOptions.SelectEventsAndUpdateState(commandResult, State, _aggregateOptions.ApplyEvent);
            _raisedEvents.AddRange(raisedEvents);

            return commandResultOptions.Coerce(commandResult, raisedEvents);
        }
    }

    public void ExecuteCommand(Action<TAggregate> commandExecutor)
    {
        if (commandExecutor == null) throw new ArgumentNullException(nameof(commandExecutor));

        if (!_aggregateOptions.CanSelectRaisedEventsFromAggregate)
        {
            throw new InvalidOperationException(
                "Cannot execute void command. Aggregate has no raised events selector specified.");
        }

        commandExecutor(State);
        var raisedEvents = _aggregateOptions.SelectRaisedEvents(State);
        _raisedEvents.Clear();
        _raisedEvents.AddRange(raisedEvents);
    }
}