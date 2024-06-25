using System.Diagnostics;

namespace DomainBlocks.V1;

[DebuggerDisplay("{EventType} <-> \"{EventName}\"")]
public class EventTypeMapping
{
    public EventTypeMapping(Type eventType, string? eventName = null, IEnumerable<string>? deprecatedEventNames = null)
    {
        EventType = eventType;
        EventName = eventName ?? eventType.Name;
        DeprecatedEventNames = deprecatedEventNames?.ToHashSet() ?? new HashSet<string>();
    }

    public Type EventType { get; }
    public string EventName { get; }
    public IReadOnlySet<string> DeprecatedEventNames { get; }
}