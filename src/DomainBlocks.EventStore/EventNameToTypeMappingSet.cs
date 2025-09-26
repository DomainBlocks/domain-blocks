using System.Collections.Frozen;
using System.Collections.Immutable;
using DomainBlocks.EventStore.Exceptions;

namespace DomainBlocks.EventStore;

internal class EventNameToTypeMappingSet
{
    private ImmutableHashSet<EventNameToTypeMapping> _mappings = [];

    public void Add<TEvent>(params string[] eventNames)
    {
        var newMappings = eventNames.Select(x => new EventNameToTypeMapping(x, typeof(TEvent)));
        var newMappingSet = _mappings.Union(newMappings);
        EnsureManyToOne(newMappingSet);
        _mappings = newMappingSet;
    }

    public FrozenDictionary<string, Type> ToFrozenDictionary()
    {
        return _mappings.ToFrozenDictionary(x => x.EventName, x => x.EventType);
    }

    private static void EnsureManyToOne(ImmutableHashSet<EventNameToTypeMapping> mappings)
    {
        var duplicateTypes = mappings
            .GroupBy(x => x.EventName)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicateTypes != null)
        {
            var types = string.Join("', '", duplicateTypes.Select(x => x.EventType));

            throw new EventTypeMapConfigurationException(
                $"Event name-to-type mappings must be many-to-one, but found name '{duplicateTypes.Key}' " +
                $"mapped to multiple types: '{types}'.");
        }
    }
}