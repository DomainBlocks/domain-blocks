using System.Collections.Frozen;

namespace DomainBlocks.EventStore.TypeMapping;

public class ReadEventTypeMap
{
    private readonly FrozenDictionary<string, Type> _map;

    internal ReadEventTypeMap(ReadEventTypeMappingSet mappings)
    {
        _map = mappings.ToFrozenDictionary();
    }

    /// <summary>
    /// Gets the event type associated with an event name.
    /// </summary>
    public Type GetEventType(string eventName) => _map.GetValueOrDefault(eventName) ??
                                                  throw new ReadEventTypeMappingNotFoundException(eventName);
}