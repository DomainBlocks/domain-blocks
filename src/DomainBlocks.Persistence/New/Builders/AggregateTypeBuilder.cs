using System;
using System.Collections.Generic;
using System.Linq;
using DomainBlocks.Aggregates;

namespace DomainBlocks.Persistence.New.Builders;

public class AggregateTypeBuilder<TAggregate, TEventBase> : IAggregateTypeBuilder
{
    private Func<TAggregate> _factory;
    private Func<TAggregate, string> _idSelector;
    private Func<string, string> _idToStreamKeySelector;
    private Func<string, string> _idToSnapshotKeySelector;
    private readonly List<ICommandResultTypeBuilder> _commandResultTypeBuilders = new();
    private readonly List<IEventTypeBuilder> _eventTypeBuilders = new();
    private Func<TAggregate, TEventBase, TAggregate> _eventApplier;

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

    // TODO: Think about how to deal with command methods that return void.
    public AggregateTypeBuilder<TAggregate, TEventBase> CommandResult<TCommandResult>(
        Action<CommandResultTypeBuilder<TAggregate, TEventBase, TCommandResult>> builderAction)
    {
        var builder = new CommandResultTypeBuilder<TAggregate, TEventBase, TCommandResult>();
        _commandResultTypeBuilders.Add(builder);
        builderAction(builder);
        return this;
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
        _eventApplier = eventApplier;
        return this;
    }

    public IAggregateType Build()
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
            _eventApplier);
    }
}