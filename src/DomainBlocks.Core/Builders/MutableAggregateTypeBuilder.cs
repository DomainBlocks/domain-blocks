using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Core.Builders;

public interface IRaisedEventsBuilder<TAggregate, TEventBase> where TEventBase : class
{
    public void WithRaisedEventsFrom(Func<TAggregate, IEnumerable<TEventBase>> eventsSelector);

    public void WithRaisedEventsFrom(
        Action<MutableCommandReturnTypeBuilder<TAggregate, TEventBase>> commandReturnTypeBuilderAction);
}

public class MutableAggregateTypeBuilder<TAggregate, TEventBase> :
    AggregateTypeBuilderBase<TAggregate, TEventBase>,
    IRaisedEventsBuilder<TAggregate, TEventBase>,
    IMutableEventApplierSource<TAggregate, TEventBase>
    where TEventBase : class
{
    private Func<TAggregate, IEnumerable<TEventBase>> _raisedEventsSelector;

    public Action<TAggregate, TEventBase> EventApplier { get; private set; }

    public IRaisedEventsBuilder<TAggregate, TEventBase> ApplyEventsWith(Action<TAggregate, TEventBase> eventApplier)
    {
        EventApplier = eventApplier;
        return this;
    }

    void IRaisedEventsBuilder<TAggregate, TEventBase>.WithRaisedEventsFrom(
        Func<TAggregate, IEnumerable<TEventBase>> eventsSelector)
    {
        _raisedEventsSelector = eventsSelector;
    }

    void IRaisedEventsBuilder<TAggregate, TEventBase>.WithRaisedEventsFrom(
        Action<MutableCommandReturnTypeBuilder<TAggregate, TEventBase>> commandReturnTypeBuilderAction)
    {
        var builder = new MutableCommandReturnTypeBuilder<TAggregate, TEventBase>(CommandReturnTypeBuilders, this);
        commandReturnTypeBuilderAction(builder);
    }

    public override IMutableAggregateType<TAggregate> Build()
    {
        var commandResultTypes = CommandReturnTypeBuilders.Select(x => x.Build());
        var eventTypes = EventTypeBuilders.Select(x => x.Build());

        return new MutableAggregateType<TAggregate, TEventBase>(
            Factory,
            IdSelector,
            IdToStreamKeySelector,
            IdToSnapshotKeySelector,
            commandResultTypes,
            eventTypes,
            EventApplier,
            _raisedEventsSelector);
    }
}