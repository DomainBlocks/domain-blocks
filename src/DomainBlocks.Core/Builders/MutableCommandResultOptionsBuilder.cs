using System;
using System.Collections.Generic;

namespace DomainBlocks.Core.Builders;

public interface IMutableCommandResultOptionsApplyEventsBuilder
{
    public void DoNotApplyEvents();
    public void ApplyEvents();
}

public sealed class MutableCommandResultOptionsBuilder<TAggregate, TEventBase, TCommandResult> :
    ICommandResultOptionsBuilder, IMutableCommandResultOptionsApplyEventsBuilder where TEventBase : class
{
    private MutableCommandResultOptions<TAggregate, TEventBase, TCommandResult> _options = new();

    ICommandResultOptions ICommandResultOptionsBuilder.Options => _options;

    public IMutableCommandResultOptionsApplyEventsBuilder WithEventsFrom(
        Func<TCommandResult, IEnumerable<TEventBase>> eventsSelector)
    {
        _options = _options.WithEventsSelector(eventsSelector);
        return this;
    }

    void IMutableCommandResultOptionsApplyEventsBuilder.DoNotApplyEvents()
    {
        _options = _options.WithApplyEventsEnabled(false);
    }

    void IMutableCommandResultOptionsApplyEventsBuilder.ApplyEvents()
    {
        _options = _options.WithApplyEventsEnabled(true);
    }
}