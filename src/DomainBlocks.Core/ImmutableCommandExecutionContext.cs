using System;
using System.Collections.Generic;

namespace DomainBlocks.Core;

public class ImmutableCommandExecutionContext<TAggregate> : ICommandExecutionContext<TAggregate>
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
        var commandResult = commandExecutor(State);
        var commandResultOptions = _aggregateOptions.GetCommandResultOptions<TCommandResult>();
        (var raisedEvents, State) =
            commandResultOptions.SelectEventsAndUpdateState(commandResult, State, _aggregateOptions.EventApplier);
        _raisedEvents.AddRange(raisedEvents);

        return commandResultOptions.Coerce(commandResult, raisedEvents);
    }

    public void ExecuteCommand(Action<TAggregate> commandExecutor)
    {
        throw new InvalidOperationException("Cannot invoke a void command on an immutable aggregate type.");
    }
}