using DomainBlocks.Experimental.EventSourcing.Extensions;

namespace DomainBlocks.Experimental.EventSourcing;

public sealed class SingleEventTypeConfigurator<TEvent, TState> :
    IEventTypeConfigurator<TState>,
    IConfigExtensionsSource
{
    private EventApplier<TState>? _eventApplier;

    public SingleEventTypeConfigurator()
    {
    }

    private SingleEventTypeConfigurator(SingleEventTypeConfigurator<TEvent, TState> copyFrom)
    {
        _eventApplier = copyFrom._eventApplier;
        ConfigExtensions = copyFrom.ConfigExtensions.Clone().ToList();
    }

    public ICollection<IConfigExtension> ConfigExtensions { get; } = new List<IConfigExtension>();

    public SingleEventTypeConfigurator<TEvent, TState> WithApplier(Func<TState, TEvent, TState> eventApplier)
    {
        _eventApplier = EventApplier.Create(eventApplier);
        return this;
    }

    public SingleEventTypeConfigurator<TEvent, TState> WithApplier(Action<TState, TEvent> eventApplier)
    {
        _eventApplier = EventApplier.Create(eventApplier);
        return this;
    }

    void IEventTypeConfigurator<TState>.Configure(EventTypeMap<TState>.Builder builder)
    {
        builder.Map<TEvent>(_eventApplier, ConfigExtensions);
    }

    IEventTypeConfiguratorBase IDeepCloneable<IEventTypeConfiguratorBase>.Clone()
    {
        return new SingleEventTypeConfigurator<TEvent, TState>(this);
    }
}