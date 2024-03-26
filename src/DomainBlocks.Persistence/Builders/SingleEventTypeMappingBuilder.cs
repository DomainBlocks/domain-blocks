using DomainBlocks.Persistence.Events;

namespace DomainBlocks.Persistence.Builders;

public class SingleEventTypeMappingBuilder<TEvent> : IEventTypeMappingBuilder
{
    private string _eventName = typeof(TEvent).Name;
    private IEnumerable<string>? _deprecatedEventNames;
    private Func<IReadOnlyDictionary<string, string>>? _metadataFactory;

    EventTypeMappingBuilderKind IEventTypeMappingBuilder.Kind => EventTypeMappingBuilderKind.SingleEvent;

    public SingleEventTypeMappingBuilder<TEvent> WithName(string eventName)
    {
        _eventName = eventName;
        return this;
    }

    public SingleEventTypeMappingBuilder<TEvent> WithDeprecatedNames(params string[] deprecatedEventNames)
    {
        _deprecatedEventNames = deprecatedEventNames;
        return this;
    }

    public SingleEventTypeMappingBuilder<TEvent> WithMetadata(Func<IReadOnlyDictionary<string, string>> metadataFactory)
    {
        _metadataFactory = metadataFactory;
        return this;
    }

    IEnumerable<EventTypeMapping> IEventTypeMappingBuilder.Build()
    {
        yield return new EventTypeMapping(typeof(TEvent), _eventName, _deprecatedEventNames);
    }
}