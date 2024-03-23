using DomainBlocks.Experimental.Persistence.Events;

namespace DomainBlocks.Experimental.Persistence.Builders;

public sealed class SingleEventTypeMappingBuilder<TEvent> : IEventTypeMappingBuilder
{
    private string _eventName = typeof(TEvent).Name;
    private IEnumerable<string>? _deprecatedEventNames;
    private Func<IReadOnlyDictionary<string, string>>? _metadataFactory;

    public EventTypeMappingBuilderKind Kind => EventTypeMappingBuilderKind.SingleEvent;

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

    public IEnumerable<EventTypeMapping> Build()
    {
        yield return new EventTypeMapping(typeof(TEvent), _eventName, _deprecatedEventNames);
    }
}