namespace DomainBlocks.Experimental.EventSourcing.Persistence;

public class ExtendedEventTypeMapping<TState>
{
    public ExtendedEventTypeMapping(
        Type eventType,
        EventApplier<TState>? eventApplier,
        string? eventName,
        IEnumerable<string>? deprecatedEventNames,
        Func<IReadOnlyDictionary<string, string>>? metadataFactory,
        EventReadCondition? readCondition,
        EventUpcaster? eventUpcaster)
    {
        EventType = eventType;
        EventApplier = eventApplier;
        EventName = eventName ?? eventType.Name;
        DeprecatedEventNames = deprecatedEventNames?.ToHashSet() ?? new HashSet<string>();
        MetadataFactory = metadataFactory;
        ReadCondition = readCondition;
        EventUpcaster = eventUpcaster;
    }

    public Type EventType { get; }
    public EventApplier<TState>? EventApplier { get; }
    public string EventName { get; }
    public IReadOnlySet<string> DeprecatedEventNames { get; }
    public Func<IReadOnlyDictionary<string, string>>? MetadataFactory { get; set; }
    public EventReadCondition? ReadCondition { get; set; }
    public EventUpcaster? EventUpcaster { get; }

    public IEnumerable<string> GetAllEventNames()
    {
        yield return EventName;

        foreach (var deprecatedEventName in DeprecatedEventNames)
        {
            yield return deprecatedEventName;
        }
    }
}