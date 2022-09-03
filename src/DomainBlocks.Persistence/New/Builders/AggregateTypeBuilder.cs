using System;
using System.Collections.Generic;
using System.Linq;

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

public class AggregateTypeBuilder<TAggregate, TEventBase> :
    IAggregateTypeBuilder,
    IIdSelectorBuilder<TAggregate>,
    IIdToStreamKeySelectorBuilder,
    IIdToSnapshotKeySelectorBuilder,
    IEventApplierSource<TAggregate, TEventBase>
    where TEventBase : class
{
    private Func<TAggregate> _factory;
    private Func<TAggregate, string> _idSelector;
    private Func<string, string> _idToStreamKeySelector;
    private Func<string, string> _idToSnapshotKeySelector;
    private readonly List<ICommandResultTypeBuilder> _commandResultTypeBuilders = new();
    private readonly List<IEventTypeBuilder> _eventTypeBuilders = new();

    private Func<TAggregate, TEventBase, TAggregate> EventApplier { get; set; }

    Func<TAggregate, TEventBase, TAggregate> IEventApplierSource<TAggregate, TEventBase>.EventApplier => EventApplier;

    public IIdSelectorBuilder<TAggregate> InitialState(Func<TAggregate> factory)
    {
        _factory = factory;
        return this;
    }

    IIdToStreamKeySelectorBuilder IIdSelectorBuilder<TAggregate>.HasId(Func<TAggregate, string> idSelector)
    {
        _idSelector = idSelector;
        return this;
    }

    IIdToSnapshotKeySelectorBuilder IIdToStreamKeySelectorBuilder.WithStreamKey(
        Func<string, string> idToStreamKeySelector)
    {
        _idToStreamKeySelector = idToStreamKeySelector;
        return this;
    }

    void IIdToSnapshotKeySelectorBuilder.WithSnapshotKey(Func<string, string> idToSnapshotKeySelector)
    {
        _idToSnapshotKeySelector = idToSnapshotKeySelector;
    }

    public CommandResultTypeBuilder<TAggregate, TEventBase, TCommandResult> CommandResult<TCommandResult>()
    {
        var builder = new CommandResultTypeBuilder<TAggregate, TEventBase, TCommandResult>(this);
        _commandResultTypeBuilders.Add(builder);
        return builder;
    }

    public VoidCommandResultTypeBuilder<TAggregate, TEventBase> VoidCommandResult()
    {
        var builder = new VoidCommandResultTypeBuilder<TAggregate, TEventBase>();
        _commandResultTypeBuilders.Add(builder);
        return builder;
    }

    public EventTypeBuilder<TEvent, TEventBase> Event<TEvent>() where TEvent : TEventBase
    {
        var builder = new EventTypeBuilder<TEvent, TEventBase>();
        _eventTypeBuilders.Add(builder);
        return builder;
    }

    public void ApplyEventsWith(Func<TAggregate, TEventBase, TAggregate> eventApplier)
    {
        EventApplier = eventApplier;
    }

    public void ApplyEventsWith(Action<TAggregate, TEventBase> eventApplier)
    {
        EventApplier = (agg, e) =>
        {
            eventApplier(agg, e);
            return agg;
        };
    }

    internal AggregateType<TAggregate, TEventBase> Build()
    {
        var commandResultTypes = _commandResultTypeBuilders.Select(x => x.Build());
        var eventTypes = _eventTypeBuilders.Select(x => x.Build());

        return new AggregateType<TAggregate, TEventBase>(
            _factory,
            _idSelector,
            _idToStreamKeySelector,
            _idToSnapshotKeySelector,
            commandResultTypes,
            eventTypes,
            EventApplier);
    }

    IAggregateType IAggregateTypeBuilder.Build() => Build();
}