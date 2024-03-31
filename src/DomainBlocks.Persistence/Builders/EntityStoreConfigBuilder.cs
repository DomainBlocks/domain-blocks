using DomainBlocks.Abstractions;

namespace DomainBlocks.Persistence.Builders;

public class EntityStoreConfigBuilder
{
    private readonly EntityAdapterRegistryBuilder _entityAdapterRegistryBuilder = new();
    private readonly Dictionary<Type, EntityStreamConfigBuilder> _entityStreamConfigBuilders = new();
    private IEventStore? _eventStore;
    private EventMapper? _eventMapper;

    public EntityStoreConfigBuilder SetEventStore(IEventStore eventStore)
    {
        _eventStore = eventStore;
        return this;
    }

    public EntityStoreConfigBuilder AddEntityAdapters(Action<EntityAdapterRegistryBuilder> builderAction)
    {
        builderAction(_entityAdapterRegistryBuilder);
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

        var entityAdapterRegistry = _entityAdapterRegistryBuilder.Build();
        var streamConfigs = _entityStreamConfigBuilders.ToDictionary(x => x.Key, x => x.Value.Build());

        return new EntityStoreConfig(_eventStore, entityAdapterRegistry, _eventMapper, streamConfigs);
    }
}