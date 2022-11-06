using System;
using System.Collections.Generic;

namespace DomainBlocks.Core;

public sealed class ImmutableCommandExecutionContext<TAggregate> : ICommandExecutionContext<TAggregate>
{
    private readonly IImmutableAggregateOptions<TAggregate> _aggregateOptions;
    private readonly List<object> _raisedEvents = new();

    public ImmutableCommandExecutionContext(
        TAggregate state, IImmutableAggregateOptions<TAggregate> aggregateOptions)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        _aggregateOptions = aggregateOptions ?? throw new ArgumentNullException(nameof(aggregateOptions));
    }

    public TAggregate State { get; private set; }
    public IReadOnlyCollection<object> RaisedEvents => _raisedEvents;

    public TCommandResult ExecuteCommand<TCommandResult>(Func<TAggregate, TCommandResult> commandExecutor)
    {
        if (commandExecutor == null) throw new ArgumentNullException(nameof(commandExecutor));

        // We attempt to get the command result options before executing the command. This ensures that we throw
        // without executing the command if the options are missing.
        var commandResultOptions = _aggregateOptions.GetCommandResultOptions<TCommandResult>();
        var commandResult = commandExecutor(State);
        (var raisedEvents, State) =
            commandResultOptions.SelectEventsAndUpdateState(commandResult, State, _aggregateOptions.ApplyEvent);
        _raisedEvents.AddRange(raisedEvents);

        return commandResultOptions.Coerce(commandResult, raisedEvents);
    }

    public void ExecuteCommand(Action<TAggregate> commandExecutor)
    {
        throw new InvalidOperationException("Cannot invoke a void command on an immutable aggregate type.");
    }
}