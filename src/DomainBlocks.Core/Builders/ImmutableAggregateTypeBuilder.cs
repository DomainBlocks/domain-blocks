using System;

namespace DomainBlocks.Core.Builders;

public interface IImmutableRaisedEventsBuilder<TAggregate, TEventBase> where TEventBase : class
{
    public void ApplyEventsWith(Func<TAggregate, TEventBase, TAggregate> eventApplier);
    public ImmutableConventionalEventApplierBuilder<TAggregate, TEventBase> ApplyEventsByConvention();
}

public class ImmutableAggregateTypeBuilder<TAggregate, TEventBase> :
    AggregateTypeBuilderBase<TAggregate, TEventBase>,
    IImmutableRaisedEventsBuilder<TAggregate, TEventBase>,
    IImmutableEventApplierSource<TAggregate, TEventBase>
    where TEventBase : class
{
    private Func<TAggregate, TEventBase, TAggregate> _eventApplier;
    private ImmutableConventionalEventApplierBuilder<TAggregate, TEventBase> _eventApplierBuilder;

    public Func<TAggregate, TEventBase, TAggregate> EventApplier
    {
        get => _eventApplier ?? _eventApplierBuilder?.Build();
        private set => _eventApplier = value;
    }

    public IImmutableRaisedEventsBuilder<TAggregate, TEventBase> WithRaisedEventsFrom(
        Action<ImmutableCommandReturnTypeBuilder<TAggregate, TEventBase>> commandReturnTypeBuilderAction)
    {
        if (commandReturnTypeBuilderAction == null)
            throw new ArgumentNullException(nameof(commandReturnTypeBuilderAction));
        
        var builder = new ImmutableCommandReturnTypeBuilder<TAggregate, TEventBase>(CommandReturnTypeBuilders, this);
        commandReturnTypeBuilderAction(builder);
        return this;
    }

    void IImmutableRaisedEventsBuilder<TAggregate, TEventBase>.ApplyEventsWith(
        Func<TAggregate, TEventBase, TAggregate> eventApplier)
    {
        EventApplier = eventApplier ?? throw new ArgumentNullException(nameof(eventApplier));
        _eventApplierBuilder = null;
    }

    ImmutableConventionalEventApplierBuilder<TAggregate, TEventBase>
        IImmutableRaisedEventsBuilder<TAggregate, TEventBase>.ApplyEventsByConvention()
    {
        _eventApplierBuilder = new ImmutableConventionalEventApplierBuilder<TAggregate, TEventBase>();
        return _eventApplierBuilder;
    }

    public override IImmutableAggregateType<TAggregate> Build()
    {
        return new ImmutableAggregateType<TAggregate, TEventBase>(
            Factory,
            IdSelector,
            IdToStreamKeySelector,
            IdToSnapshotKeySelector,
            CommandReturnTypes,
            EventTypes,
            EventApplier);
    }
}