using System;
using System.Collections.Generic;

namespace DomainBlocks.Core.Builders;

public interface IMutableCommandResultOptionsApplyEventsBuilder
{
    public void ApplyEvents();
}

public class MutableCommandResultOptionsBuilder<TAggregate, TEventBase, TCommandResult> :
    ICommandResultOptionsBuilder, IMutableCommandResultOptionsApplyEventsBuilder where TEventBase : class
{
    private MutableCommandResultOptions<TAggregate, TEventBase, TCommandResult> _options = new();

    public ICommandResultOptions Options => _options;

    public IMutableCommandResultOptionsApplyEventsBuilder WithEventsFrom(
        Func<TCommandResult, IEnumerable<TEventBase>> eventsSelector)
    {
        _options = _options.WithEventsSelector(eventsSelector);
        return this;
    }

    void IMutableCommandResultOptionsApplyEventsBuilder.ApplyEvents()
    {
        _options = _options.WithApplyEventsEnabled(true);
    }
}