using DomainBlocks.Experimental.EventSourcing.Persistence.Adapters;
using DomainBlocks.Experimental.EventSourcing.Persistence.Extensions;

namespace DomainBlocks.Experimental.EventSourcing.Persistence.Configuration;

public sealed class EntityStoreBuilder
{
    private readonly List<GenericEntityAdapterFactory> _genericEntityAdapterFactories = new();
    private readonly List<IEntityAdapter> _entityAdapters = new();
    private readonly EntityStoreOptionsBuilder _optionsBuilder = new();
    private EntityStoreFactory? _factory;

    public EntityStoreBuilder SetInfrastructure<TReadEvent, TWriteEvent, TRawData, TStreamVersion>(
        IEventStore<TReadEvent, TWriteEvent, TStreamVersion> eventStore,
        IEventAdapter<TReadEvent, TWriteEvent, TStreamVersion, TRawData> eventAdapter,
        EntityStoreOptions<TRawData> dataOptions)
        where TStreamVersion : struct
    {
        _factory = (entityAdapterProvider, options) =>
            EntityStore.Create(eventStore, eventAdapter, entityAdapterProvider, options, dataOptions);

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

    public EntityStoreBuilder Configure(Action<EntityStoreOptionsBuilder> builderAction)
    {
        builderAction(_optionsBuilder);
        return this;
    }

    public EntityStoreBuilder Configure<TEntity>(Action<EntityStreamOptionsBuilder> builderAction)
    {
        var builder = _optionsBuilder.For<TEntity>();
        builderAction(builder);
        return this;
    }

    public IEntityStore Build()
    {
        if (_factory == null) throw new InvalidOperationException();

        // TODO: Consider auto-configuring, e.g.
        // var genericEntityAdapterFactories = AppDomain.CurrentDomain
        //     .GetAssemblies()
        //     .Where(x => !x.FullName?.StartsWith("System") ?? false)
        //     .SelectMany(x => x.GetTypes())
        //     .Where(x => x.IsGenericTypeDefinition &&
        //                 x.GetInterfaces().Any(i => i.IsGenericType &&
        //                                            i.GetGenericTypeDefinition() == typeof(IEntityAdapter<,>)))
        //     .Select(x => new GenericEntityAdapterFactory(x))
        //     .ToList();

        var entityAdapterProvider = new EntityAdapterProvider(_entityAdapters, _genericEntityAdapterFactories);
        var options = _optionsBuilder.Build();

        return _factory(entityAdapterProvider, options);
    }
}