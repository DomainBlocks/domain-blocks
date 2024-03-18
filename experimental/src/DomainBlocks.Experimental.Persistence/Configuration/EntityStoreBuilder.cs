using DomainBlocks.Experimental.Persistence.Entities;
using DomainBlocks.Experimental.Persistence.Extensions;
using DomainBlocks.Experimental.Persistence.Events;

namespace DomainBlocks.Experimental.Persistence.Configuration;

public sealed class EntityStoreBuilder
{
    private readonly List<GenericEntityAdapterFactory> _genericEntityAdapterFactories = new();
    private readonly List<IEntityAdapter> _entityAdapters = new();
    private readonly EntityStoreConfigBuilder _configBuilder = new();
    private IEventStore? _eventStore;
    private IWriteEventFactory? _writeEventFactory;

    public EntityStoreBuilder SetInfrastructure(IEventStore eventStore, IWriteEventFactory writeEventFactory)
    {
        _eventStore = eventStore;
        _writeEventFactory = writeEventFactory;
        return this;
    }

    public EntityStoreBuilder AddEntityAdapterType(Type entityAdapterType, params object?[]? constructorArgs)
    {
        var factory = new GenericEntityAdapterFactory(entityAdapterType, constructorArgs);
        _genericEntityAdapterFactories.Add(factory);
        return this;
    }

    public EntityStoreBuilder AddEntityAdapter<TEntity, TState>(IEntityAdapter<TEntity, TState> entityAdapter)
    {
        _entityAdapters.Add(entityAdapter.HideStateType());
        return this;
    }

    public EntityStoreBuilder Configure(Action<EntityStoreConfigBuilder> builderAction)
    {
        builderAction(_configBuilder);
        return this;
    }

    public EntityStoreBuilder Configure<TEntity>(Action<EntityStreamConfigBuilder> builderAction)
    {
        var builder = _configBuilder.For<TEntity>();
        builderAction(builder);
        return this;
    }

    public EntityStore Build()
    {
        if (_eventStore == null) throw new InvalidOperationException();
        if (_writeEventFactory == null) throw new InvalidOperationException();

        var entityAdapterProvider = new EntityAdapterProvider(_entityAdapters, _genericEntityAdapterFactories);
        var config = _configBuilder.Build();

        return new EntityStore(_eventStore, _writeEventFactory, entityAdapterProvider, config);
    }
}