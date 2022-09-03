using System;
using System.Collections.Generic;

namespace DomainBlocks.Persistence.New.Builders;

public interface ICommandResultUpdatedStateSelectorBuilder<TAggregate, out TCommandResult>
{
    public void WithUpdatedStateFrom(Func<TCommandResult, TAggregate, TAggregate> updatedStateSelector);
    public void ApplyEvents();
    public void ApplyEventsWhileEnumerating();
}

public class CommandResultTypeBuilder<TAggregate, TEventBase, TCommandResult> :
    ICommandResultTypeBuilder,
    ICommandResultUpdatedStateSelectorBuilder<TAggregate, TCommandResult>
    where TEventBase : class
{
    private readonly IEventApplierSource<TAggregate, TEventBase> _eventApplierSource;
    private Func<TCommandResult, TAggregate, IEnumerable<TEventBase>> _eventsSelector;
    private Func<TCommandResult, TAggregate, TAggregate> _updatedStateSelector;
    private ApplyEventsBehavior? _applyEventsBehavior;

    internal CommandResultTypeBuilder(IEventApplierSource<TAggregate, TEventBase> eventApplierSource)
    {
        _eventApplierSource = eventApplierSource;
    }

    public ICommandResultUpdatedStateSelectorBuilder<TAggregate, TCommandResult> WithEventsFrom(
        Func<TCommandResult, TAggregate, IEnumerable<TEventBase>> eventsSelector)
    {
        _eventsSelector = eventsSelector;
        return this;
    }

    void ICommandResultUpdatedStateSelectorBuilder<TAggregate, TCommandResult>.WithUpdatedStateFrom(
        Func<TCommandResult, TAggregate, TAggregate> updatedStateSelector)
    {
        _updatedStateSelector = updatedStateSelector;
    }

    void ICommandResultUpdatedStateSelectorBuilder<TAggregate, TCommandResult>.ApplyEvents()
    {
        _applyEventsBehavior = ApplyEventsBehavior.ApplyAfterEnumerating;
    }

    void ICommandResultUpdatedStateSelectorBuilder<TAggregate, TCommandResult>.ApplyEventsWhileEnumerating()
    {
        _applyEventsBehavior = ApplyEventsBehavior.ApplyWhileEnumerating;
    }

    ICommandResultType ICommandResultTypeBuilder.Build()
    {
        var strategy = _applyEventsBehavior switch
        {
            null => DefaultCommandResultStrategy.Create(_updatedStateSelector, _eventsSelector),
            ApplyEventsBehavior.ApplyAfterEnumerating =>
                ApplyAfterEnumeratingStrategy.Create(_eventsSelector, _eventApplierSource.EventApplier),
            ApplyEventsBehavior.ApplyWhileEnumerating =>
                ApplyWhileEnumeratingStrategy.Create(_eventsSelector, _eventApplierSource.EventApplier),
            _ => null
        };

        return new CommandResultType<TAggregate, TEventBase, TCommandResult>(strategy);
    }

    private enum ApplyEventsBehavior
    {
        ApplyAfterEnumerating,
        ApplyWhileEnumerating
    }
}