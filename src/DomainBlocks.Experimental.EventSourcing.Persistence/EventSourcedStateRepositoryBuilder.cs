namespace DomainBlocks.Experimental.EventSourcing.Persistence;

public sealed class EventSourcedStateRepositoryBuilder
{
    internal EventSourcedStateRepositoryBuilder()
    {
    }

    public EventSourcedStateRepositoryBuilder<TRawData> Use<TReadEvent, TWriteEvent, TRawData, TStreamVersion>(
        IEventRepository<TReadEvent, TWriteEvent, TStreamVersion> eventRepository,
        IEventAdapter<TReadEvent, TWriteEvent, TRawData, TStreamVersion> eventAdapter)
        where TStreamVersion : struct
    {
        if (eventRepository == null) throw new ArgumentNullException(nameof(eventRepository));
        if (eventAdapter == null) throw new ArgumentNullException(nameof(eventAdapter));

        return new EventSourcedStateRepositoryBuilder<TRawData>(
            (bindings, defaults) =>
                EventSourcedStateRepository.Create(bindings, defaults, eventRepository, eventAdapter));
    }
}

public sealed class EventSourcedStateRepositoryBuilder<TRawData>
{
    private readonly EventSourcedStateRepositoryFactory<TRawData> _factory;

    private ConfigurationMode _configurationMode = ConfigurationMode.Dynamic;

    private StateEventStreamBindingDefaults<TRawData> _defaults = StateEventStreamBindingDefaults<TRawData>.Default;

    private readonly Dictionary<Type, IStateEventStreamBindingBuilder> _bindingBuilders = new();

    internal EventSourcedStateRepositoryBuilder(EventSourcedStateRepositoryFactory<TRawData> factory)
    {
        _factory = factory;
    }

    public EventSourcedStateRepositoryBuilder<TRawData> SetConfigurationMode(ConfigurationMode configurationMode)
    {
        _configurationMode = configurationMode;
        return this;
    }

    public EventSourcedStateRepositoryBuilder<TRawData> ConfigureDefaults(
        Action<StateEventStreamBindingDefaults<TRawData>.Builder> builderAction)
    {
        var builder = _defaults.ToBuilder();
        builderAction(builder);
        _defaults = builder.Build();
        return this;
    }

    public EventSourcedStateRepositoryBuilder<TRawData> Configure<TState>(
        Action<StateEventStreamBinding<TState, TRawData>.Builder> builderAction)
    {
        if (builderAction == null) throw new ArgumentNullException(nameof(builderAction));

        if (!_bindingBuilders.TryGetValue(typeof(TState), out var builder))
        {
            var concreteBuilder = StateEventStreamBinding.CreateBuilder<TState, TRawData>();
            _defaults.ApplyTo(concreteBuilder);
            _bindingBuilders.Add(typeof(TState), concreteBuilder);
            builder = concreteBuilder;
        }

        builderAction((StateEventStreamBinding<TState, TRawData>.Builder)builder);

        return this;
    }

    public EventSourcedStateRepositoryBuilder<TRawData> Configure<TState>() => Configure<TState>(_ => { });

    public IEventSourcedStateRepository Build()
    {
        var bindings = _bindingBuilders.Values.Select(x => x.Build());
        var defaults = _configurationMode == ConfigurationMode.Dynamic ? _defaults : null;

        return _factory(bindings, defaults);
    }
}