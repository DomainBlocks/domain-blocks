using System;
using System.Collections.Generic;

namespace DomainBlocks.Persistence.New.Builders;

public interface IAggregateTypeBuilder
{
    IAggregateType Build();
}

public interface IIdSelectorBuilder<out TAggregate>
{
    public IIdToStreamKeySelectorBuilder HasId(Func<TAggregate, string> idSelector);
}

public interface IIdToStreamKeySelectorBuilder
{
    public IIdToSnapshotKeySelectorBuilder WithStreamKey(Func<string, string> idToStreamKeySelector);
}

public interface IIdToSnapshotKeySelectorBuilder
{
    public void WithSnapshotKey(Func<string, string> idToSnapshotKeySelector);
}

public abstract class AggregateTypeBuilderBase<TAggregate, TEventBase> :
    IAggregateTypeBuilder,
    IIdSelectorBuilder<TAggregate>,
    IIdToStreamKeySelectorBuilder,
    IIdToSnapshotKeySelectorBuilder
    where TEventBase : class
{
    protected Func<TAggregate> Factory { get; private set; }
    protected Func<TAggregate, string> IdSelector { get; private set; }
    protected Func<string, string> IdToStreamKeySelector { get; private set; }
    protected Func<string, string> IdToSnapshotKeySelector { get; private set; }
    protected List<ICommandReturnTypeBuilder> CommandReturnTypeBuilders { get; } = new();
    protected List<IEventTypeBuilder> EventTypeBuilders { get; } = new();

    public IIdSelectorBuilder<TAggregate> InitialState(Func<TAggregate> factory)
    {
        Factory = factory;
        return this;
    }

    IIdToStreamKeySelectorBuilder IIdSelectorBuilder<TAggregate>.HasId(Func<TAggregate, string> idSelector)
    {
        IdSelector = idSelector;
        return this;
    }

    IIdToSnapshotKeySelectorBuilder IIdToStreamKeySelectorBuilder.WithStreamKey(
        Func<string, string> idToStreamKeySelector)
    {
        IdToStreamKeySelector = idToStreamKeySelector;
        return this;
    }

    void IIdToSnapshotKeySelectorBuilder.WithSnapshotKey(Func<string, string> idToSnapshotKeySelector)
    {
        IdToSnapshotKeySelector = idToSnapshotKeySelector;
    }

    public EventTypeBuilder<TEvent, TEventBase> Event<TEvent>() where TEvent : TEventBase
    {
        var builder = new EventTypeBuilder<TEvent, TEventBase>();
        EventTypeBuilders.Add(builder);
        return builder;
    }

    public abstract IAggregateType Build();
}