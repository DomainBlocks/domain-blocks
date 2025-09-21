using System.Collections.Frozen;

namespace DomainBlocks.EventStore;

public sealed class EventTypeMap
{
    private readonly FrozenDictionary<Type, string> _writeMap;
    private readonly FrozenDictionary<string, Type> _readMap;

    internal EventTypeMap(
        IEnumerable<EventTypeToNameMapping> writeMappings,
        IEnumerable<EventNameToTypeMapping> readMappings)
    {
        var writeMappingArray = writeMappings.Distinct().ToArray();
        var readMappingArray = readMappings.Distinct().ToArray();

        EnsureWriteMappingsAreOneToOne(writeMappingArray);
        EnsureReadMappingsAreManyToOne(readMappingArray);

        _writeMap = writeMappingArray.ToFrozenDictionary(x => x.EventType, x => x.EventName);
        _readMap = readMappingArray.ToFrozenDictionary(x => x.EventName, x => x.EventType);
    }

    public string GetEventName(Type eventType) => _writeMap.GetValueOrDefault(eventType) ??
                                                  throw new EventTypeMappingNotFoundException(eventType);

    public Type GetEventType(string eventName) => _readMap.GetValueOrDefault(eventName) ??
                                                  throw new EventTypeMappingNotFoundException(eventName);

    private static void EnsureWriteMappingsAreOneToOne(EventTypeToNameMapping[] writeMappings)
    {
        var duplicateNames = writeMappings
            .GroupBy(x => x.EventType)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicateNames != null)
        {
            var names = string.Join("', '", duplicateNames.Select(x => x.EventName));

            throw new ArgumentException(
                $"Event type-to-name mappings for writes must be one-to-one, but found type '{duplicateNames.Key}' " +
                $"mapped to multiple names: '{names}'",
                nameof(writeMappings));
        }

        var duplicateTypes = writeMappings
            .GroupBy(x => x.EventName)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicateTypes != null)
        {
            var types = string.Join("', '", duplicateTypes.Select(x => x.EventType));

            throw new ArgumentException(
                $"Event type-to-name mappings for writes must be one-to-one, but found name '{duplicateTypes.Key}' " +
                $"mapped from multiple types: '{types}'",
                nameof(writeMappings));
        }
    }

    private static void EnsureReadMappingsAreManyToOne(EventNameToTypeMapping[] readMappings)
    {
        var duplicateTypes = readMappings
            .GroupBy(x => x.EventName)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicateTypes != null)
        {
            var types = string.Join("', '", duplicateTypes.Select(x => x.EventType));

            throw new ArgumentException(
                $"Event name-to-type mappings for reads must be many-to-one, but found name '{duplicateTypes.Key}' " +
                $"mapped to multiple types: '{types}'");
        }
    }
}