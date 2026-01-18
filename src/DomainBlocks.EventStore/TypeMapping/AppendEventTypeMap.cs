using System.Collections.Frozen;

namespace DomainBlocks.EventStore.TypeMapping;

public class AppendEventTypeMap
{
    private readonly FrozenDictionary<Type, string> _map;

    internal AppendEventTypeMap(AppendEventTypeMappingSet mappings)
    {
        _map = mappings.ToFrozenDictionary();
    }

    /// <summary>
    /// Gets the event name associated with an event type.
    /// </summary>
    public string GetEventName(Type eventType) => _map.GetValueOrDefault(eventType) ??
                                                  throw new AppendEventTypeMappingNotFoundException(eventType);
}