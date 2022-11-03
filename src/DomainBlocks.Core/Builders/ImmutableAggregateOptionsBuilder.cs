using System;

namespace DomainBlocks.Core.Builders;

public interface IImmutableRaisedEventsBuilder<TAggregate, TEventBase> where TEventBase : class
{
    public void ApplyEventsWith(Func<TAggregate, TEventBase, TAggregate> eventApplier);
    public ImmutableConventionalEventApplierBuilder<TAggregate, TEventBase> ApplyEventsByConvention();
}

public class ImmutableAggregateOptionsBuilder<TAggregate, TEventBase> :
    AggregateOptionsBuilderBase<TAggregate, TEventBase>,
    IImmutableRaisedEventsBuilder<TAggregate, TEventBase>
    where TEventBase : class
{
    private ImmutableAggregateOptions<TAggregate, TEventBase> _options = new();
    private ImmutableConventionalEventApplierBuilder<TAggregate, TEventBase> _eventApplierBuilder;

    protected override AggregateOptionsBase<TAggregate, TEventBase> Options
    {
        get => _eventApplierBuilder == null ? _options : _options.WithEventApplier(_eventApplierBuilder.Build());
        set => _options = (ImmutableAggregateOptions<TAggregate, TEventBase>)value;
    }

    public IImmutableRaisedEventsBuilder<TAggregate, TEventBase> WithRaisedEventsFrom(
        Action<ImmutableCommandResultOptionsBuilder<TAggregate, TEventBase>> commandResultBuilderAction)
    {
        if (commandResultBuilderAction == null)
            throw new ArgumentNullException(nameof(commandResultBuilderAction));

        var builder = new ImmutableCommandResultOptionsBuilder<TAggregate, TEventBase>();
        commandResultBuilderAction(builder);
        Options = _options.WithCommandResultOptions(builder.Options);
        return this;
    }

    void IImmutableRaisedEventsBuilder<TAggregate, TEventBase>.ApplyEventsWith(
        Func<TAggregate, TEventBase, TAggregate> eventApplier)
    {
        _eventApplierBuilder = null;
        Options = _options.WithEventApplier(eventApplier);
    }

    ImmutableConventionalEventApplierBuilder<TAggregate, TEventBase>
        IImmutableRaisedEventsBuilder<TAggregate, TEventBase>.ApplyEventsByConvention()
    {
        _eventApplierBuilder = new ImmutableConventionalEventApplierBuilder<TAggregate, TEventBase>();
        return _eventApplierBuilder;
    }
}