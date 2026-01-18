using System.Collections.Frozen;
using System.Collections.Immutable;

namespace DomainBlocks.EventStore.TypeMapping;

internal class ReadEventTypeMappingSet
{
    private ImmutableHashSet<ReadEventTypeMapping> _mappings = [];

    public void Add<TEvent>(params string[] eventNames)
    {
        var newMappings = eventNames.Select(x => new ReadEventTypeMapping(x, typeof(TEvent)));
        var newMappingSet = _mappings.Union(newMappings);
        EnsureManyToOne(newMappingSet);
        _mappings = newMappingSet;
    }

    public FrozenDictionary<string, Type> ToFrozenDictionary()
    {
        return _mappings.ToFrozenDictionary(x => x.EventName, x => x.EventType);
    }

    private static void EnsureManyToOne(ImmutableHashSet<ReadEventTypeMapping> mappings)
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