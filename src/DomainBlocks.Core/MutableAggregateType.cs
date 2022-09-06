using System;
using System.Collections.Generic;

namespace DomainBlocks.Core;

public interface IMutableAggregateType<TAggregate> : IAggregateType<TAggregate>
{
    public bool CanSelectRaisedEventsFromAggregate { get; }

    public new void ApplyEvent(TAggregate aggregate, object @event);
    public IMutableCommandReturnType<TAggregate, TCommandResult> GetCommandReturnType<TCommandResult>();
    public IEnumerable<object> SelectRaisedEvents(TAggregate aggregate);
}

public class MutableAggregateType<TAggregate, TEventBase>
    : AggregateTypeBase<TAggregate, TEventBase>, IMutableAggregateType<TAggregate> where TEventBase : class
{
    private readonly Action<TAggregate, TEventBase> _eventApplier;
    private readonly Func<TAggregate, IEnumerable<TEventBase>> _raisedEventsSelector;

    public MutableAggregateType(
        Func<TAggregate> factory,
        Func<TAggregate, string> idSelector,
        Func<string, string> idToStreamKeySelector,
        Func<string, string> idToSnapshotKeySelector,
        IEnumerable<ICommandReturnType> commandReturnTypes,
        IEnumerable<IEventType> eventTypes,
        Action<TAggregate, TEventBase> eventApplier,
        Func<TAggregate, IEnumerable<TEventBase>> raisedEventsSelector)
        : base(factory, idSelector, idToStreamKeySelector, idToSnapshotKeySelector, commandReturnTypes, eventTypes)
    {
        _eventApplier = eventApplier;
        _raisedEventsSelector = raisedEventsSelector;
    }

    public bool CanSelectRaisedEventsFromAggregate => _raisedEventsSelector != null;

    public override TAggregate ApplyEvent(TAggregate aggregate, object @event)
    {
        ((IMutableAggregateType<TAggregate>)this).ApplyEvent(aggregate, @event);
        return aggregate;
    }

    void IMutableAggregateType<TAggregate>.ApplyEvent(TAggregate aggregate, object @event)
    {
        _eventApplier(aggregate, (TEventBase)@event);
    }

    public override ICommandExecutionContext<TAggregate> GetCommandExecutionContext(TAggregate aggregate)
    {
        return new MutableCommandExecutionContext<TAggregate>(aggregate, this);
    }

    public new IMutableCommandReturnType<TAggregate, TCommandResult> GetCommandReturnType<TCommandResult>()
    {
        return (IMutableCommandReturnType<TAggregate, TCommandResult>)base.GetCommandReturnType<TCommandResult>();
    }

    public IEnumerable<object> SelectRaisedEvents(TAggregate aggregate)
    {
        if (!CanSelectRaisedEventsFromAggregate)
        {
            throw new InvalidOperationException("Aggregate has no events selector specified");
        }

        return _raisedEventsSelector(aggregate);
    }
}