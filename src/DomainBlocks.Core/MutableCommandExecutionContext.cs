using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Core;

public sealed class MutableCommandExecutionContext<TAggregate, TEventBase> : ICommandExecutionContext<TAggregate>
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

        if (_aggregateOptions.CanSelectRaisedEventsFromAggregate)
        {
            var commandResult = commandExecutor(State);
            var raisedEvents = _aggregateOptions.SelectRaisedEvents(State);
            _raisedEvents.Clear();
            _raisedEvents.AddRange(raisedEvents);

            return commandResult;
        }

        {
            // We attempt to get the command result options before executing the command. This ensures that we throw
            // without executing the command if the options are missing.
            var commandResultOptions = _aggregateOptions.GetCommandResultOptions<TCommandResult>();
            var commandResult = commandExecutor(State);
            var raisedEvents =
                commandResultOptions.SelectEventsAndUpdateState(commandResult, State, _aggregateOptions.ApplyEvent);
            _raisedEvents.AddRange(raisedEvents);

            // If the command result is an event enumerable, return the materialized events to avoid multiple
            // enumeration.
            return typeof(TCommandResult) == typeof(IEnumerable<TEventBase>)
                ? (TCommandResult)raisedEvents.Cast<TEventBase>()
                : commandResult;
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