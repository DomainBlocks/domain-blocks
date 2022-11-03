using System;
using System.Collections.Generic;

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
    private MutableAggregateType<TAggregate, TEventBase> _options = new();
    private MutableConventionalEventApplierBuilder<TAggregate, TEventBase> _eventApplierBuilder;

    public Action<TAggregate, TEventBase> EventApplier => (agg, e) => Options.ApplyEvent(agg, e);

    protected override AggregateTypeBase<TAggregate, TEventBase> Options
    {
        get => _eventApplierBuilder == null ? _options : _options.WithEventApplier(_eventApplierBuilder.Build());
        set => _options = (MutableAggregateType<TAggregate, TEventBase>)value;
    }

    public IMutableRaisedEventsBuilder<TAggregate, TEventBase> WithRaisedEventsFrom(
        Func<TAggregate, IEnumerable<TEventBase>> eventsSelector)
    {
        _options = _options.WithRaisedEventsSelector(eventsSelector);
        return this;
    }

    public IMutableRaisedEventsBuilder<TAggregate, TEventBase> WithRaisedEventsFrom(
        Action<MutableCommandReturnTypeBuilder<TAggregate, TEventBase>> commandReturnTypeBuilderAction)
    {
        if (commandReturnTypeBuilderAction == null)
            throw new ArgumentNullException(nameof(commandReturnTypeBuilderAction));

        var builder = new MutableCommandReturnTypeBuilder<TAggregate, TEventBase>(this);
        commandReturnTypeBuilderAction(builder);
        Options = _options.WithCommandReturnTypes(builder.Options);
        return this;
    }

    void IMutableRaisedEventsBuilder<TAggregate, TEventBase>.ApplyEventsWith(
        Action<TAggregate, TEventBase> eventApplier)
    {
        _eventApplierBuilder = null;
        _options = _options.WithEventApplier(eventApplier);
    }

    MutableConventionalEventApplierBuilder<TAggregate, TEventBase>
        IMutableRaisedEventsBuilder<TAggregate, TEventBase>.ApplyEventsByConvention()
    {
        _eventApplierBuilder = new MutableConventionalEventApplierBuilder<TAggregate, TEventBase>();
        return _eventApplierBuilder;
    }
}