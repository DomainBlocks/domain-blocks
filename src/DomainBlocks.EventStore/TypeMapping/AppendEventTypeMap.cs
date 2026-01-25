using System.Collections.Frozen;

namespace DomainBlocks.EventStore.TypeMapping;

public class AppendEventTypeMap
{
    private readonly FrozenDictionary<Type, string> _map;

    private AppendEventTypeMap(Dictionary<Type, string> mappings)
    {
        EnsureOneToOne(mappings);

        _map = mappings.ToFrozenDictionary();
    }

    public string GetEventName(Type eventType) => _map.GetValueOrDefault(eventType) ??
                                                  throw new AppendEventTypeMappingNotFoundException(eventType);

    private static void EnsureOneToOne(Dictionary<Type, string> mappings)
    {
        var nameConflict = mappings
            .GroupBy(x => x.Value)
            .FirstOrDefault(g => g.Count() > 1);

        if (nameConflict != null)
        {
            var types = string.Join("', '", nameConflict.Select(x => x.Key));

            throw new EventTypeMapConfigurationException(
                $"Event type-to-name mappings must be one-to-one, but found name '{nameConflict.Key}' " +
                $"mapped from multiple types: '{types}'.");
        }
    }

    public sealed class Builder
    {
        private readonly Dictionary<Type, string> _mappings = [];

        internal Builder()
        {
        }

        public Builder MapType<TEvent>(Action<Mapping>? configure = null)
        {
            var eventType = typeof(TEvent);

            var mapping = new Mapping(eventType);
            configure?.Invoke(mapping);

            _mappings[eventType] = mapping.EventName;

            return this;
        }

        internal AppendEventTypeMap Build() => new(_mappings);

        public sealed class Mapping
        {
            internal Mapping(Type eventType)
            {
                EventName = eventType.Name;
            }

            public void ToName(string name)
            {
                EventName = name;
            }

            internal string EventName { get; private set; }
        }
    }
}