using System;
using System.Collections.Generic;

namespace DomainBlocks.Persistence.New.Builders;

public interface IEventsCommandResultTypeBuilder<TAggregate, TEventBase, out TCommandResult>
{
    public IUpdatedStateCommandResultTypeBuilder<TAggregate, TEventBase, TCommandResult> WithEventsFrom(
        Func<TCommandResult, TAggregate, IEnumerable<TEventBase>> eventsSelector);
}

public interface IUpdatedStateCommandResultTypeBuilder<TAggregate, out TEventBase, out TCommandResult>
{
    public ICommandResultTypeBuilder WithUpdatedStateFrom(
        Func<TCommandResult, TAggregate, TAggregate> updatedStateSelector);

    public ICommandResultTypeBuilder UpdateStateWithAppliedEvents();
}

public class CommandResultTypeBuilder<TAggregate, TEventBase, TCommandResult> :
    ICommandResultTypeBuilder,
    IEventsCommandResultTypeBuilder<TAggregate, TEventBase, TCommandResult>,
    IUpdatedStateCommandResultTypeBuilder<TAggregate, TEventBase, TCommandResult>
{
    private readonly AggregateTypeBuilder<TAggregate, TEventBase> _aggregateTypeBuilder;
    private Func<TCommandResult, TAggregate, IEnumerable<TEventBase>> _eventsSelector;
    private Func<TCommandResult, TAggregate, TAggregate> _updatedStateSelector;
    private bool _hasApplyEventsEnabled;

    public CommandResultTypeBuilder(AggregateTypeBuilder<TAggregate, TEventBase> aggregateTypeBuilder)
    {
        _aggregateTypeBuilder = aggregateTypeBuilder;
    }

    public IUpdatedStateCommandResultTypeBuilder<TAggregate, TEventBase, TCommandResult> WithEventsFrom(
        Func<TCommandResult, TAggregate, IEnumerable<TEventBase>> eventsSelector)
    {
        _eventsSelector = eventsSelector;
        return this;
    }

    public ICommandResultTypeBuilder WithUpdatedStateFrom(
        Func<TCommandResult, TAggregate, TAggregate> updatedStateSelector)
    {
        _updatedStateSelector = updatedStateSelector;
        return this;
    }

    public ICommandResultTypeBuilder UpdateStateWithAppliedEvents()
    {
        _hasApplyEventsEnabled = true;
        return this;
    }

    public CommandResultType<TAggregate, TEventBase, TCommandResult> Build()
    {
        ICommandResultStrategy<TAggregate, TEventBase, TCommandResult> strategy = _hasApplyEventsEnabled
            ? ApplyEventsCommandResultStrategy.Create(_eventsSelector, _aggregateTypeBuilder.EventApplier)
            : DefaultCommandResultStrategy.Create(_updatedStateSelector, _eventsSelector);

        return new CommandResultType<TAggregate, TEventBase, TCommandResult>(strategy);
    }

    ICommandResultType ICommandResultTypeBuilder.Build() => Build();
}