using DomainBlocks.Experimental.Persistence.Extensions;
using DomainBlocks.Experimental.Persistence.Adapters;

namespace DomainBlocks.Experimental.Persistence.Configuration;

public sealed class EntityStoreBuilder
{
    private readonly List<GenericEntityAdapterFactory> _genericEntityAdapterFactories = new();
    private readonly List<IEntityAdapter> _entityAdapters = new();
    private readonly EntityStoreConfigBuilder _configBuilder = new();
    private EntityStoreFactory? _factory;

    public EntityStoreBuilder SetInfrastructure<TReadEvent, TWriteEvent>(
        IEventStore<TReadEvent, TWriteEvent> eventStore,
        IEventAdapter<TReadEvent, TWriteEvent> eventAdapter)
    {
        _factory = (entityAdapterProvider, config) =>
            EntityStore.Create(eventStore, eventAdapter, entityAdapterProvider, config);

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

    public IEntityStore Build()
    {
        if (_factory == null) throw new InvalidOperationException();

        var entityAdapterProvider = new EntityAdapterProvider(_entityAdapters, _genericEntityAdapterFactories);
        var config = _configBuilder.Build();

        return _factory(entityAdapterProvider, config);
    }
}