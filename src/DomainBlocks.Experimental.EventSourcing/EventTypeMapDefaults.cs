using DomainBlocks.Experimental.EventSourcing.Extensions;

namespace DomainBlocks.Experimental.EventSourcing;

public sealed class EventTypeMapDefaults
{
    public static readonly EventTypeMapDefaults Default = new(Enumerable.Empty<IEventTypeConfigurator>());
    private readonly IReadOnlyCollection<IEventTypeConfigurator> _configurators;

    private EventTypeMapDefaults(IEnumerable<IEventTypeConfigurator> configurators)
    {
        _configurators = configurators
            .Clone()
            .Cast<IEventTypeConfigurator>()
            .ToList()
            .AsReadOnly();
    }

    private IEnumerable<IEventTypeConfigurator> Configurators => _configurators
        .Clone()
        .Cast<IEventTypeConfigurator>();

    public void ApplyTo<TState>(EventTypeMap<TState>.Builder builder)
    {
        builder.AddConfigurators(Configurators);
    }

    public Builder ToBuilder() => new(this);

    public sealed class Builder
    {
        private readonly List<IEventTypeConfigurator> _configurators;

        internal Builder(EventTypeMapDefaults defaults)
        {
            _configurators = defaults.Configurators.ToList();
        }

        public AllEventTypeConfigurator<TEventBase> MapAll<TEventBase>()
        {
            var builder = new AllEventTypeConfigurator<TEventBase>();
            _configurators.Add(builder);
            return builder;
        }

        public AllEventTypeConfigurator<object> MapAll() => MapAll<object>();

        public EventTypeMapDefaults Build() => new(_configurators);
    }
}