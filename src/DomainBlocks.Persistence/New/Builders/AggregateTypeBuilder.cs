using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Persistence.New.Builders;

public class AggregateTypeBuilder<TAggregate, TEventBase> : IAggregateTypeBuilder
{
    private Func<TAggregate> _factory;
    private Func<TAggregate, string> _idSelector;
    private Func<string, string> _idToStreamKeySelector;
    private Func<string, string> _idToSnapshotKeySelector;
    private readonly List<ICommandResultTypeBuilder> _commandResultTypeBuilders = new();
    private readonly List<IEventTypeBuilder> _eventTypeBuilders = new();
    
    public Func<TAggregate, TEventBase, TAggregate> EventApplier { get; private set; }

    public AggregateTypeBuilder<TAggregate, TEventBase> InitialState(Func<TAggregate> factory)
    {
        _factory = factory;
        return this;
    }

    public AggregateTypeBuilder<TAggregate, TEventBase> HasId(Func<TAggregate, string> idSelector)
    {
        _idSelector = idSelector;
        return this;
    }

    public AggregateTypeBuilder<TAggregate, TEventBase> WithStreamKey(Func<string, string> idToStreamKeySelector)
    {
        _idToStreamKeySelector = idToStreamKeySelector;
        return this;
    }

    public AggregateTypeBuilder<TAggregate, TEventBase> WithSnapshotKey(Func<string, string> idToSnapshotKeySelector)
    {
        _idToSnapshotKeySelector = idToSnapshotKeySelector;
        return this;
    }

    public IEventsCommandResultTypeBuilder<TAggregate, TEventBase, TCommandResult> CommandResult<TCommandResult>()
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

    public AggregateTypeBuilder<TAggregate, TEventBase> ApplyEventsWith(
        Func<TAggregate, TEventBase, TAggregate> eventApplier)
    {
        EventApplier = eventApplier;
        return this;
    }

    public AggregateType<TAggregate, TEventBase> Build()
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