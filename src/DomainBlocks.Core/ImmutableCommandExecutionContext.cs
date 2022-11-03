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
    public IEnumerable<object> RaisedEvents => _raisedEvents;

    public TCommandResult ExecuteCommand<TCommandResult>(Func<TAggregate, TCommandResult> commandExecutor)
    {
        var commandResult = commandExecutor(State);
        var options = _aggregateOptions.GetCommandResultOptions<TCommandResult>();
        (var events, State) = options.SelectEventsAndUpdateState(commandResult, State, _aggregateOptions.EventApplier);
        _raisedEvents.AddRange(events);

        return commandResult;
    }

    public void ExecuteCommand(Action<TAggregate> commandExecutor)
    {
        throw new InvalidOperationException("Cannot invoke a void command on an immutable aggregate type.");
    }
}