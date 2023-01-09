using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Core;

public sealed class ImmutableCommandExecutionContext<TAggregate, TEventBase> : ICommandExecutionContext<TAggregate>
{
    private readonly IImmutableAggregateOptions<TAggregate> _aggregateOptions;
    private readonly List<object> _raisedEvents = new();
    private TAggregate _state;

    public ImmutableCommandExecutionContext(
        TAggregate state, IImmutableAggregateOptions<TAggregate> aggregateOptions)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _aggregateOptions = aggregateOptions ?? throw new ArgumentNullException(nameof(aggregateOptions));
    }

    public TAggregate State => _state;
    public IReadOnlyCollection<object> RaisedEvents => _raisedEvents;

    public TCommandResult ExecuteCommand<TCommandResult>(Func<TAggregate, TCommandResult> commandExecutor)
    {
        if (commandExecutor == null) throw new ArgumentNullException(nameof(commandExecutor));

        // We attempt to get the command result options before executing the command. This ensures that we throw
        // without executing the command if the options are missing.
        var commandResultOptions = _aggregateOptions.GetCommandResultOptions<TCommandResult>();
        var commandResult = commandExecutor(State);
        var raisedEvents =
            commandResultOptions.SelectEventsAndUpdateState(commandResult, ref _state, _aggregateOptions.ApplyEvent);
        _raisedEvents.AddRange(raisedEvents);

        // If the command result is an event enumerable, return the materialized events to avoid multiple enumeration.
        return typeof(TCommandResult) == typeof(IEnumerable<TEventBase>)
            ? (TCommandResult)raisedEvents.Cast<TEventBase>()
            : commandResult;
    }

    public void ExecuteCommand(Action<TAggregate> commandExecutor)
    {
        throw new InvalidOperationException("Cannot invoke a void command on an immutable aggregate type.");
    }
}