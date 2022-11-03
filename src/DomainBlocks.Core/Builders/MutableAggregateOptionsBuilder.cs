using System;
using System.Collections.Generic;

namespace DomainBlocks.Core.Builders;

public interface IMutableRaisedEventsBuilder<TAggregate, TEventBase> where TEventBase : class
{
    public void ApplyEventsWith(Action<TAggregate, TEventBase> eventApplier);
    public MutableConventionalEventApplierBuilder<TAggregate, TEventBase> ApplyEventsByConvention();
}

public class MutableAggregateOptionsBuilder<TAggregate, TEventBase> :
    AggregateOptionsBuilderBase<TAggregate, TEventBase>,
    IMutableRaisedEventsBuilder<TAggregate, TEventBase>
    where TEventBase : class
{
    private MutableAggregateOptions<TAggregate, TEventBase> _options = new();
    private MutableConventionalEventApplierBuilder<TAggregate, TEventBase> _eventApplierBuilder;

    protected override AggregateOptionsBase<TAggregate, TEventBase> Options
    {
        get => _eventApplierBuilder == null ? _options : _options.WithEventApplier(_eventApplierBuilder.Build());
        set => _options = (MutableAggregateOptions<TAggregate, TEventBase>)value;
    }

    public IMutableRaisedEventsBuilder<TAggregate, TEventBase> WithRaisedEventsFrom(
        Func<TAggregate, IEnumerable<TEventBase>> eventsSelector)
    {
        _options = _options.WithRaisedEventsSelector(eventsSelector);
        return this;
    }

    public IMutableRaisedEventsBuilder<TAggregate, TEventBase> WithRaisedEventsFrom(
        Action<MutableCommandResultOptionsBuilder<TAggregate, TEventBase>> commandResultBuilderAction)
    {
        if (commandResultBuilderAction == null)
            throw new ArgumentNullException(nameof(commandResultBuilderAction));

        var builder = new MutableCommandResultOptionsBuilder<TAggregate, TEventBase>();
        commandResultBuilderAction(builder);
        Options = _options.WithCommandResultOptions(builder.Options);
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