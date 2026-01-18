using System.Collections.Frozen;
using System.Collections.Immutable;

namespace DomainBlocks.EventStore.TypeMapping;

internal class AppendEventTypeMappingSet
{
    private ImmutableHashSet<AppendEventTypeMapping> _mappings = [];

    public void Add<TEvent>(string eventName)
    {
        var newMappings = _mappings.Add(new AppendEventTypeMapping(typeof(TEvent), eventName));
        EnsureOneToOne(newMappings);
        _mappings = newMappings;
    }

    public FrozenDictionary<Type, string> ToFrozenDictionary()
    {
        return _mappings.ToFrozenDictionary(x => x.EventType, x => x.EventName);
    }

    private static void EnsureOneToOne(ImmutableHashSet<AppendEventTypeMapping> mappings)
    {
        var duplicateNames = mappings
            .GroupBy(x => x.EventType)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicateNames != null)
        {
            var names = string.Join("', '", duplicateNames.Select(x => x.EventName));

            throw new EventTypeMapConfigurationException(
                $"Event type-to-name mappings must be one-to-one, but found type '{duplicateNames.Key}' " +
                $"mapped to multiple names: '{names}'.");
        }

        var duplicateTypes = mappings
            .GroupBy(x => x.EventName)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicateTypes != null)
        {
            var types = string.Join("', '", duplicateTypes.Select(x => x.EventType));

            throw new EventTypeMapConfigurationException(
                $"Event type-to-name mappings must be one-to-one, but found name '{duplicateTypes.Key}' " +
                $"mapped from multiple types: '{types}'.");
        }
    }
}