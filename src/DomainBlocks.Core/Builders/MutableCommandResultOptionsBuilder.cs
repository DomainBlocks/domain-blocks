using System;
using System.Collections.Generic;

namespace DomainBlocks.Core.Builders;

public interface IMutableCommandResultOptionsApplyEventsBuilder
{
    /// <summary>
    /// Specify to not apply the returned events to the aggregate. Use this option when the mutable aggregate's state
    /// is updated as a result of invoking the command method. This option is the default behaviour.
    /// </summary>
    public void DoNotApplyEvents();
    
    /// <summary>
    /// Specify to update the aggregate's state by applying the returned events. Use this option when the mutable
    /// aggregate's state is not updated as a result of invoking the command method.
    /// </summary>
    public void ApplyEvents();
}

public sealed class MutableCommandResultOptionsBuilder<TAggregate, TEventBase, TCommandResult> :
    ICommandResultOptionsBuilder, IMutableCommandResultOptionsApplyEventsBuilder where TEventBase : class
{
    private MutableCommandResultOptions<TAggregate, TEventBase, TCommandResult> _options = new();

    ICommandResultOptions ICommandResultOptionsBuilder.Options => _options;

    /// <summary>
    /// Specify where to select the events from in the command result object.
    /// </summary>
    /// <returns>
    /// An object that can be used to further configure the command result type.
    /// </returns>
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