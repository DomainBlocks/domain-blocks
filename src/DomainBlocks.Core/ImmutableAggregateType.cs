using System;
using System.Collections.Generic;

namespace DomainBlocks.Core;

public interface IImmutableAggregateType<TAggregate> : IAggregateType<TAggregate>
{
    public IImmutableCommandReturnType<TAggregate, TCommandResult> GetCommandReturnType<TCommandResult>();
}

public class ImmutableAggregateType<TAggregate, TEventBase>
    : AggregateTypeBase<TAggregate, TEventBase>, IImmutableAggregateType<TAggregate> where TEventBase : class
{
    private readonly Func<TAggregate, TEventBase, TAggregate> _eventApplier;

    public ImmutableAggregateType(
        Func<TAggregate> factory,
        Func<TAggregate, string> idSelector,
        Func<string, string> idToStreamKeySelector,
        Func<string, string> idToSnapshotKeySelector,
        IEnumerable<ICommandReturnType> commandReturnTypes,
        IEnumerable<IEventType> eventTypes,
        Func<TAggregate, TEventBase, TAggregate> eventApplier)
        : base(factory, idSelector, idToStreamKeySelector, idToSnapshotKeySelector, commandReturnTypes, eventTypes)
    {
        _eventApplier = eventApplier;
    }

    public override TAggregate ApplyEvent(TAggregate aggregate, object @event)
    {
        return _eventApplier(aggregate, (TEventBase)@event);
    }

    public override ICommandExecutionContext<TAggregate> GetCommandExecutionContext(TAggregate aggregate)
    {
        return new ImmutableCommandExecutionContext<TAggregate>(aggregate, this);
    }

    public new IImmutableCommandReturnType<TAggregate, TCommandResult> GetCommandReturnType<TCommandResult>()
    {
        return (IImmutableCommandReturnType<TAggregate, TCommandResult>)base.GetCommandReturnType<TCommandResult>();
    }
}