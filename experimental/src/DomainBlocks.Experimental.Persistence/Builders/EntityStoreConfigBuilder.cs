using DomainBlocks.Experimental.Persistence.Entities;
using DomainBlocks.Experimental.Persistence.Events;

namespace DomainBlocks.Experimental.Persistence.Builders;

public class EntityStoreConfigBuilder
{
    private readonly List<GenericEntityAdapterFactory> _genericEntityAdapterFactories = new();
    private readonly List<IEntityAdapter> _entityAdapters = new();
    private readonly Dictionary<Type, EntityStreamConfigBuilder> _entityStreamConfigBuilders = new();
    private IEventStore? _eventStore;
    private EventMapper? _eventMapper;

    public EntityStoreConfigBuilder SetEventStore(IEventStore eventStore)
    {
        _eventStore = eventStore;
        return this;
    }

    public EntityStoreConfigBuilder AddGenericEntityAdapterType(
        Type entityAdapterType, params object?[]? constructorArgs)
    {
        var factory = new GenericEntityAdapterFactory(entityAdapterType, constructorArgs);
        _genericEntityAdapterFactories.Add(factory);
        return this;
    }

    public EntityStoreConfigBuilder AddEntityAdapter<TEntity>(IEntityAdapter<TEntity> entityAdapter)
    {
        _entityAdapters.Add(entityAdapter);
        return this;
    }

    public EntityStoreConfigBuilder MapEvents(Action<EventMapperBuilder> builderAction)
    {
        var builder = new EventMapperBuilder();
        builderAction(builder);
        _eventMapper = builder.Build();
        return this;
    }

    public EntityStoreConfigBuilder SetEventMapper(EventMapper eventMapper)
    {
        _eventMapper = eventMapper;
        return this;
    }

    public EntityStoreConfigBuilder Configure<TEntity>(Action<EntityStreamConfigBuilder> builderAction)
    {
        if (!_entityStreamConfigBuilders.TryGetValue(typeof(TEntity), out var builder))
        {
            builder = new EntityStreamConfigBuilder(typeof(TEntity));
            _entityStreamConfigBuilders.Add(typeof(TEntity), builder);
        }

        builderAction(builder);
        return this;
    }

    public EntityStoreConfig Build()
    {
        if (_eventStore == null) throw new InvalidOperationException();
        if (_eventMapper == null) throw new InvalidOperationException();

        var entityAdapterProvider = new EntityAdapterProvider(_entityAdapters, _genericEntityAdapterFactories);
        var streamConfigs = _entityStreamConfigBuilders.Values.Select(x => x.Build());

        return new EntityStoreConfig(_eventStore, entityAdapterProvider, _eventMapper, streamConfigs);
    }
}