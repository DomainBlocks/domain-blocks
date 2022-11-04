using System;
using System.Collections.Generic;

namespace DomainBlocks.Core;

public interface IMutableAggregateOptions<TAggregate> : IAggregateOptions<TAggregate>
{
    public new Action<TAggregate, object> EventApplier { get; }
    public bool CanSelectRaisedEventsFromAggregate { get; }

    public IMutableCommandResultOptions<TAggregate, TCommandResult> GetCommandResultOptions<TCommandResult>();
    public IEnumerable<object> SelectRaisedEvents(TAggregate aggregate);
}

public class MutableAggregateOptions<TAggregate, TEventBase> :
    AggregateOptionsBase<TAggregate, TEventBase>,
    IMutableAggregateOptions<TAggregate> where TEventBase : class
{
    private Func<TAggregate, IReadOnlyCollection<TEventBase>> _raisedEventsSelector;

    public MutableAggregateOptions()
    {
    }

    private MutableAggregateOptions(MutableAggregateOptions<TAggregate, TEventBase> copyFrom) : base(copyFrom)
    {
        _raisedEventsSelector = copyFrom._raisedEventsSelector;
    }

    public new Action<TAggregate, object> EventApplier => (agg, e) => base.EventApplier(agg, e);
    
    public bool CanSelectRaisedEventsFromAggregate => _raisedEventsSelector != null;

    public MutableAggregateOptions<TAggregate, TEventBase> WithEventApplier(Action<TAggregate, TEventBase> eventApplier)
    {
        if (eventApplier == null) throw new ArgumentNullException(nameof(eventApplier));
        
        return (MutableAggregateOptions<TAggregate, TEventBase>)WithEventApplier((agg, e) =>
        {
            eventApplier(agg, e);
            return agg;
        });
    }

    public MutableAggregateOptions<TAggregate, TEventBase> WithRaisedEventsSelector(
        Func<TAggregate, IReadOnlyCollection<TEventBase>> raisedEventsSelector)
    {
        if (raisedEventsSelector == null) throw new ArgumentNullException(nameof(raisedEventsSelector));
        
        var clone = Clone();
        clone._raisedEventsSelector = raisedEventsSelector;
        return clone;
    }

    public override ICommandExecutionContext<TAggregate> CreateCommandExecutionContext(TAggregate aggregate)
    {
        return new MutableCommandExecutionContext<TAggregate>(aggregate, this);
    }

    public new IMutableCommandResultOptions<TAggregate, TCommandResult> GetCommandResultOptions<TCommandResult>()
    {
        var commandResultOptions = base.GetCommandResultOptions<TCommandResult>();
        return (IMutableCommandResultOptions<TAggregate, TCommandResult>)commandResultOptions;
    }

    public IEnumerable<object> SelectRaisedEvents(TAggregate aggregate)
    {
        if (!CanSelectRaisedEventsFromAggregate)
        {
            throw new InvalidOperationException("Aggregate has no events selector specified");
        }

        return _raisedEventsSelector(aggregate);
    }

    protected override MutableAggregateOptions<TAggregate, TEventBase> Clone()
    {
        return new MutableAggregateOptions<TAggregate, TEventBase>(this);
    }
}