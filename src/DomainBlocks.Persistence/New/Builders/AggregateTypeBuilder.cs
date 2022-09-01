using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Persistence.New.Builders;

public class AggregateTypeBuilder<TAggregate>
{
    private readonly ModelBuilder _modelBuilder;

    public AggregateTypeBuilder(ModelBuilder modelBuilder)
    {
        _modelBuilder = modelBuilder;
    }

    public AggregateTypeBuilder<TAggregate, TEventBase> EventBaseType<TEventBase>()
    {
        var builder = new AggregateTypeBuilder<TAggregate, TEventBase>();
        _modelBuilder.AddAggregateConfigurationBuilder(builder);
        return builder;
    }
}

public class AggregateTypeBuilder<TAggregate, TEventBase> : IAggregateTypeBuilder
{
    private Func<TAggregate> _factory;
    private Func<TAggregate, string> _idSelector;
    private Func<string, string> _idToStreamKeySelector;
    private Func<string, string> _idToSnapshotKeySelector;
    private readonly List<ICommandResultTypeBuilder> _commandResultConfigBuilders = new();
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
    public AggregateTypeBuilder<TAggregate, TEventBase> HasCommandResult<TCommandResult>(
        Action<CommandResultTypeBuilder<TAggregate, TEventBase, TCommandResult>> builderAction)
    {
        var builder = new CommandResultTypeBuilder<TAggregate, TEventBase, TCommandResult>();
        _commandResultConfigBuilders.Add(builder);
        builderAction(builder);
        return this;
    }

    public AggregateTypeBuilder<TAggregate, TEventBase> ApplyEventsWith(
        Func<TAggregate, TEventBase, TAggregate> eventApplier)
    {
        _eventApplier = eventApplier;
        return this;
    }

    public IAggregateType Build()
    {
        var commandResultConfigs = _commandResultConfigBuilders.Select(x => x.Build());

        return new AggregateType<TAggregate, TEventBase>(
            _factory,
            _idSelector,
            _idToStreamKeySelector,
            _idToSnapshotKeySelector,
            commandResultConfigs,
            _eventApplier);
    }
}