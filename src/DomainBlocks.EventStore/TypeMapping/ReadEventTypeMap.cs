using System.Collections.Frozen;

namespace DomainBlocks.EventStore.TypeMapping;

public class ReadEventTypeMap
{
    private readonly FrozenDictionary<string, Type> _map;

    private ReadEventTypeMap(Dictionary<Type, string[]> mappings)
    {
        EnsureManyToOne(mappings);

        _map = mappings
            .SelectMany(x => x.Value.Select(name => (type: x.Key, name)))
            .Distinct()
            .ToFrozenDictionary(x => x.name, x => x.type);
    }

    public Type GetEventType(string eventName) => _map.GetValueOrDefault(eventName) ??
                                                  throw new ReadEventTypeMappingNotFoundException(eventName);

    private static void EnsureManyToOne(Dictionary<Type, string[]> mappings)
    {
        var typeConflict = mappings
            .SelectMany(x => x.Value.Select(name => (type: x.Key, name)))
            .Distinct()
            .GroupBy(x => x.name)
            .FirstOrDefault(g => g.Count() > 1);

        if (typeConflict != null)
        {
            var types = string.Join("', '", typeConflict.Select(x => x.type));

            throw new EventTypeMapConfigurationException(
                $"Event name-to-type mappings must be many-to-one, but found name '{typeConflict.Key}' " +
                $"mapped to multiple types: '{types}'.");
        }
    }

    public sealed class Builder
    {
        private readonly Dictionary<Type, string[]> _mappings = [];

        public Builder MapType<TEvent>(Action<Mapping>? configure = null)
        {
            var eventType = typeof(TEvent);
            var mapping = new Mapping(eventType);

            configure?.Invoke(mapping);

            _mappings[eventType] = mapping.EventNames;

            return this;
        }

        public ReadEventTypeMap Build() => new(_mappings);

        public class Mapping
        {
            internal Mapping(Type eventType)
            {
                EventNames = [eventType.Name];
            }

            internal string[] EventNames { get; private set; }

            public void FromNames(params string[] names)
            {
                if (names.Length == 0)
                    throw new ArgumentException("At least one name must be provided.", nameof(names));

                EventNames = names;
            }
        }
    }
}