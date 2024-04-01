namespace DomainBlocks.V1.Persistence.Builders;

public class SingleEventTypeMappingBuilder<TEvent> : IEventTypeMappingBuilder
{
    private string _eventName = typeof(TEvent).Name;
    private IEnumerable<string>? _deprecatedEventNames;

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

    IEnumerable<EventTypeMapping> IEventTypeMappingBuilder.Build()
    {
        yield return new EventTypeMapping(typeof(TEvent), _eventName, _deprecatedEventNames);
    }
}