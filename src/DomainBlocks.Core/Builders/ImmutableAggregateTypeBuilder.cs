using System;

namespace DomainBlocks.Core.Builders;

public interface IImmutableRaisedEventsBuilder<TAggregate, TEventBase> where TEventBase : class
{
    public void ApplyEventsWith(Func<TAggregate, TEventBase, TAggregate> eventApplier);
    public ImmutableConventionalEventApplierBuilder<TAggregate, TEventBase> ApplyEventsByConvention();
}

public class ImmutableAggregateTypeBuilder<TAggregate, TEventBase> :
    AggregateTypeBuilderBase<TAggregate, TEventBase>,
    IImmutableRaisedEventsBuilder<TAggregate, TEventBase>
    where TEventBase : class
{
    private ImmutableAggregateType<TAggregate, TEventBase> _options = new();
    private ImmutableConventionalEventApplierBuilder<TAggregate, TEventBase> _eventApplierBuilder;

    protected override AggregateTypeBase<TAggregate, TEventBase> Options
    {
        get => _eventApplierBuilder == null ? _options : _options.WithEventApplier(_eventApplierBuilder.Build());
        set => _options = (ImmutableAggregateType<TAggregate, TEventBase>)value;
    }

    public IImmutableRaisedEventsBuilder<TAggregate, TEventBase> WithRaisedEventsFrom(
        Action<ImmutableCommandReturnTypeBuilder<TAggregate, TEventBase>> commandReturnTypeBuilderAction)
    {
        if (commandReturnTypeBuilderAction == null)
            throw new ArgumentNullException(nameof(commandReturnTypeBuilderAction));

        var builder = new ImmutableCommandReturnTypeBuilder<TAggregate, TEventBase>();
        commandReturnTypeBuilderAction(builder);
        Options = _options.WithCommandReturnTypes(builder.Options);
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