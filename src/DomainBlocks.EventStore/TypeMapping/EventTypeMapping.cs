namespace DomainBlocks.EventStore.TypeMapping;

public sealed class EventTypeMapping
{
    private EventTypeMapping(Type eventType, string? writeName, IReadOnlyList<string> readNames)
    {
        EventType = eventType;
        WriteName = writeName;
        ReadNames = readNames;
    }

    public Type EventType { get; }

    public string? WriteName { get; }

    public IReadOnlyList<string> ReadNames { get; }

    public static EventTypeMapping ReadWrite<TEvent>(string? name = null) => ReadWrite(typeof(TEvent), name);

    public static EventTypeMapping ReadWrite(Type eventType, string? name = null)
    {
        var resolvedName = ResolveName(eventType, name);
        return new EventTypeMapping(eventType, resolvedName, [resolvedName]);
    }

    public static EventTypeMapping WriteOnly<TEvent>(string? name = null) => WriteOnly(typeof(TEvent), name);

    public static EventTypeMapping WriteOnly(Type eventType, string? name = null) =>
        new(eventType, ResolveName(eventType, name), []);

    public static EventTypeMapping ReadOnly<TEvent>(params string[] names) => ReadOnly(typeof(TEvent), names);

    public static EventTypeMapping ReadOnly(Type eventType, params string[] names)
    {
        if (names.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Names cannot be null or whitespace.", nameof(names));

        return new EventTypeMapping(eventType, null, names.Length == 0 ? [eventType.Name] : [.. names]);
    }

    public override string ToString()
    {
        var writeName = WriteName is null ? "<none>" : $"'{WriteName}'";

        var readNames = ReadNames.Count == 0
            ? "<none>"
            : $"['{string.Join("', '", ReadNames)}']";

        return $"{EventType.Name}: WriteName={writeName}, ReadNames={readNames}";
    }

    private static string ResolveName(Type eventType, string? name)
    {
        if (name is not null && string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be empty or whitespace.", nameof(name));

        return name ?? eventType.Name;
    }
}