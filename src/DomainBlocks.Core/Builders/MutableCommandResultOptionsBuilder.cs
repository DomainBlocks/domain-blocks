using System;
using System.Collections.Generic;

namespace DomainBlocks.Core.Builders;

public interface IApplyRaisedEventsBehaviorBuilder
{
    public void ApplyEvents();
    public void ApplyEventsWhileEnumerating();
}

public class MutableCommandResultOptionsBuilder<TAggregate, TEventBase, TCommandResult> :
    ICommandResultOptionsBuilder, IApplyRaisedEventsBehaviorBuilder where TEventBase : class
{
    private MutableCommandResultOptions<TAggregate, TEventBase, TCommandResult> _options = new();

    public ICommandResultOptions Options => _options;

    public IApplyRaisedEventsBehaviorBuilder WithEventsFrom(
        Func<TCommandResult, IEnumerable<TEventBase>> eventsSelector)
    {
        _options = _options.WithEventsSelector(eventsSelector);
        return this;
    }
    
    void IApplyRaisedEventsBehaviorBuilder.ApplyEvents()
    {
        _options = _options.WithApplyRaisedEventsBehavior(ApplyRaisedEventsBehavior.ApplyAfterEnumerating);
    }

    void IApplyRaisedEventsBehaviorBuilder.ApplyEventsWhileEnumerating()
    {
        _options = _options.WithApplyRaisedEventsBehavior(ApplyRaisedEventsBehavior.ApplyWhileEnumerating);
    }
}