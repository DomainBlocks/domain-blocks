using System.Collections.Frozen;
using DomainBlocks.Core.Exceptions;

namespace DomainBlocks.EventStore.TypeMapping;

/// <summary>
/// Defines the mapping between event CLR types and their string names used in storage.
/// </summary>
public sealed class EventTypeMap
{
    private readonly FrozenDictionary<Type, string> _writes;
    private readonly FrozenDictionary<string, Type> _reads;

    private EventTypeMap(FrozenDictionary<Type, string> writes, FrozenDictionary<string, Type> reads)
    {
        _writes = writes;
        _reads = reads;
    }

    public string GetEventName(Type eventType) =>
        _writes.GetValueOrDefault(eventType) ?? throw new EventTypeNotMappedException(eventType);

    public Type GetEventType(string eventName) =>
        _reads.GetValueOrDefault(eventName) ?? throw new EventNameNotMappedException(eventName);

    public static EventTypeMap Create(params EventTypeMapping[] mappings)
    {
        var writes = new Dictionary<Type, string>();
        var reads = new Dictionary<string, Type>();

        foreach (var mapping in mappings)
        {
            if (mapping.WriteName is { } writeName && !writes.TryAdd(mapping.EventType, writeName))
            {
                throw new DomainBlocksException(
                    $"Cannot add event type mapping ({mapping}): " +
                    $"type '{mapping.EventType.Name}' is already mapped to name '{writes[mapping.EventType]}'.");
            }

            foreach (var name in mapping.ReadNames)
            {
                if (!reads.TryAdd(name, mapping.EventType))
                {
                    throw new DomainBlocksException(
                        $"Cannot add event type mapping ({mapping}): " +
                        $"name '{name}' is already mapped to type '{reads[name].Name}'.");
                }
            }
        }

        return new EventTypeMap(writes.ToFrozenDictionary(), reads.ToFrozenDictionary());
    }
}