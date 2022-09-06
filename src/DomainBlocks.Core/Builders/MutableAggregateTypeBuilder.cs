using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Core.Builders;

public interface IMutableRaisedEventsBuilder<TAggregate, TEventBase> where TEventBase : class
{
    public void ApplyEventsWith(Action<TAggregate, TEventBase> eventApplier);
    public MutableConventionalEventApplierBuilder<TAggregate, TEventBase> ApplyEventsByConvention();
}

public class MutableAggregateTypeBuilder<TAggregate, TEventBase> :
    AggregateTypeBuilderBase<TAggregate, TEventBase>,
    IMutableRaisedEventsBuilder<TAggregate, TEventBase>,
    IMutableEventApplierSource<TAggregate, TEventBase>
    where TEventBase : class
{
    private Func<TAggregate, IEnumerable<TEventBase>> _raisedEventsSelector;
    private Action<TAggregate, TEventBase> _eventApplier;
    private MutableConventionalEventApplierBuilder<TAggregate, TEventBase> _eventApplierBuilder;

    public Action<TAggregate, TEventBase> EventApplier
    {
        get => _eventApplier ?? _eventApplierBuilder?.Build();
        private set => _eventApplier = value;
    }

    public IMutableRaisedEventsBuilder<TAggregate, TEventBase> WithRaisedEventsFrom(
        Func<TAggregate, IEnumerable<TEventBase>> eventsSelector)
    {
        _raisedEventsSelector = eventsSelector ?? throw new ArgumentNullException(nameof(eventsSelector));
        return this;
    }

    public IMutableRaisedEventsBuilder<TAggregate, TEventBase> WithRaisedEventsFrom(
        Action<MutableCommandReturnTypeBuilder<TAggregate, TEventBase>> commandReturnTypeBuilderAction)
    {
        if (commandReturnTypeBuilderAction == null)
            throw new ArgumentNullException(nameof(commandReturnTypeBuilderAction));

        var builder = new MutableCommandReturnTypeBuilder<TAggregate, TEventBase>(CommandReturnTypeBuilders, this);
        commandReturnTypeBuilderAction(builder);
        return this;
    }

    void IMutableRaisedEventsBuilder<TAggregate, TEventBase>.ApplyEventsWith(
        Action<TAggregate, TEventBase> eventApplier)
    {
        EventApplier = eventApplier ?? throw new ArgumentNullException(nameof(eventApplier));
        _eventApplierBuilder = null;
    }

    MutableConventionalEventApplierBuilder<TAggregate, TEventBase>
        IMutableRaisedEventsBuilder<TAggregate, TEventBase>.ApplyEventsByConvention()
    {
        _eventApplierBuilder = new MutableConventionalEventApplierBuilder<TAggregate, TEventBase>();
        return _eventApplierBuilder;
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