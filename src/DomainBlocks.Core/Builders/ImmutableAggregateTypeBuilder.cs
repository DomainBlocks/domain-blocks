using System;
using System.Linq;

namespace DomainBlocks.Core.Builders;

public interface IImmutableRaisedEventsBuilder<TAggregate, out TEventBase> where TEventBase : class
{
    public void ApplyEventsWith(Func<TAggregate, TEventBase, TAggregate> eventApplier);
}

public class ImmutableAggregateTypeBuilder<TAggregate, TEventBase> :
    AggregateTypeBuilderBase<TAggregate, TEventBase>,
    IImmutableRaisedEventsBuilder<TAggregate, TEventBase>,
    IImmutableEventApplierSource<TAggregate, TEventBase>
    where TEventBase : class
{
    public Func<TAggregate, TEventBase, TAggregate> EventApplier { get; private set; }

    public IImmutableRaisedEventsBuilder<TAggregate, TEventBase> WithRaisedEventsFrom(
        Action<ImmutableCommandReturnTypeBuilder<TAggregate, TEventBase>> commandReturnTypeBuilderAction)
    {
        var builder = new ImmutableCommandReturnTypeBuilder<TAggregate, TEventBase>(CommandReturnTypeBuilders, this);
        commandReturnTypeBuilderAction(builder);
        return this;
    }

    void IImmutableRaisedEventsBuilder<TAggregate, TEventBase>.ApplyEventsWith(
        Func<TAggregate, TEventBase, TAggregate> eventApplier)
    {
        EventApplier = eventApplier;
    }

    public override IImmutableAggregateType<TAggregate> Build()
    {
        var commandResultTypes = CommandReturnTypeBuilders.Select(x => x.Build());
        var eventTypes = EventTypeBuilders.Select(x => x.Build());

        return new ImmutableAggregateType<TAggregate, TEventBase>(
            Factory,
            IdSelector,
            IdToStreamKeySelector,
            IdToSnapshotKeySelector,
            commandResultTypes,
            eventTypes,
            EventApplier);
    }
}