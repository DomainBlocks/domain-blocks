using System.Collections.Frozen;
using DomainBlocks.Core.Exceptions;

namespace DomainBlocks.EventStore.TypeMapping;

public sealed class EventTypeMap
{
    private readonly FrozenDictionary<Type, EventTypeMapping.Write> _writes;
    private readonly FrozenDictionary<string, EventTypeMapping.Read> _reads;

    private EventTypeMap(
        FrozenDictionary<Type, EventTypeMapping.Write> writes,
        FrozenDictionary<string, EventTypeMapping.Read> reads)
    {
        _writes = writes;
        _reads = reads;
    }

    public string GetEventName(Type eventType) =>
        _writes.GetValueOrDefault(eventType)?.EventName ?? throw new EventTypeNotMappedException(eventType);

    public EventTypeMapping.Read GetReadMapping(string eventName) =>
        _reads.GetValueOrDefault(eventName) ?? throw new EventNameNotMappedException(eventName);

    internal static EventTypeMap Create(IEnumerable<EventTypeMapping> mappings)
    {
        var writes = new Dictionary<Type, EventTypeMapping.Write>();
        var reads = new Dictionary<string, EventTypeMapping.Read>();

        foreach (var mapping in mappings)
        {
            switch (mapping)
            {
                case EventTypeMapping.Write write when !writes.TryAdd(write.EventType, write):
                    throw new DomainBlocksException(
                        $"Cannot add [{write}]: conflicts with [{writes[write.EventType]}].");

                case EventTypeMapping.Read read when !reads.TryAdd(read.EventName, read):
                    throw new DomainBlocksException($"Cannot add [{read}]: conflicts with [{reads[read.EventName]}].");
            }
        }

        return new EventTypeMap(writes.ToFrozenDictionary(), reads.ToFrozenDictionary(StringComparer.Ordinal));
    }

    internal void ValidateAssignableTo(Type targetType)
    {
        foreach (var mapping in _writes.Values.Concat<EventTypeMapping>(_reads.Values))
            mapping.ValidateAssignableTo(targetType);
    }
}