using System;
using System.Collections.Generic;

namespace DomainBlocks.Core.Builders;

public interface IImmutableCommandResultUpdatedStateSelectorBuilder<in TAggregate, out TCommandResult>
{
    public void WithUpdatedStateFrom(Func<TCommandResult, TAggregate> updatedStateSelector);
}

public class ImmutableCommandResultOptionsBuilder<TAggregate, TEventBase, TCommandResult> :
    ICommandResultOptionsBuilder,
    IImmutableCommandResultUpdatedStateSelectorBuilder<TAggregate, TCommandResult>
    where TEventBase : class
{
    private ImmutableCommandResultOptions<TAggregate, TEventBase, TCommandResult> _options = new();

    public ICommandResultOptions Options => _options;

    public IImmutableCommandResultUpdatedStateSelectorBuilder<TAggregate, TCommandResult> WithEventsFrom(
        Func<TCommandResult, IEnumerable<TEventBase>> eventsSelector)
    {
        _options = _options.WithEventsSelector(eventsSelector);
        return this;
    }

    void IImmutableCommandResultUpdatedStateSelectorBuilder<TAggregate, TCommandResult>.WithUpdatedStateFrom(
        Func<TCommandResult, TAggregate> updatedStateSelector)
    {
        _options = _options.WithUpdatedStateSelector(updatedStateSelector);
    }
}