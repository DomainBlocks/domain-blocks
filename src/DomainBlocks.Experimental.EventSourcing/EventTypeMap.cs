using System.Collections;
using DomainBlocks.Experimental.EventSourcing.Extensions;

namespace DomainBlocks.Experimental.EventSourcing;

public static class EventTypeMap
{
    public static EventTypeMap<TState>.Builder CreateBuilder<TState>()
    {
        return EventTypeMap<TState>.Empty.ToBuilder();
    }
}

/// <summary>
/// Represents a collection of <see cref="EventTypeMapping{TState}"/> objects keyed by event type.
/// </summary>
/// <typeparam name="TState">The state type to which each event type mapping applies.</typeparam>
public class EventTypeMap<TState> : IEnumerable<EventTypeMapping<TState>>
{
    public static readonly EventTypeMap<TState> Empty = new(new Dictionary<Type, EventTypeMapping<TState>>());

    private readonly IReadOnlyDictionary<Type, EventTypeMapping<TState>> _items;

    private EventTypeMap(IReadOnlyDictionary<Type, EventTypeMapping<TState>> items)
    {
        _items = items;
    }

    public EventTypeMapping<TState> this[Type eventType]
    {
        get
        {
            if (!_items.TryGetValue(eventType, out var item))
            {
                throw new ArgumentException($"Mapping not found for event type '{eventType}'.", nameof(eventType));
            }

            return item;
        }
    }

    public Builder ToBuilder() => new(this);

    public sealed class Builder
    {
        private readonly List<IEventTypeConfiguratorBase> _configurators = new();
        private readonly Dictionary<Type, EventTypeMapping<TState>.Builder> _items;

        public Builder(EventTypeMap<TState> config)
        {
            _items = config._items.ToDictionary(x => x.Key, x => x.Value.ToBuilder());
        }

        public AllEventTypeConfigurator<TEventBase, TState> MapAll<TEventBase>()
        {
            var configurator = new AllEventTypeConfigurator<TEventBase, TState>();
            _configurators.Add(configurator);
            return configurator;
        }

        public AllEventTypeConfigurator<object, TState> MapAll() => MapAll<object>();

        public SingleEventTypeConfigurator<TEvent, TState> Map<TEvent>()
        {
            var configurator = new SingleEventTypeConfigurator<TEvent, TState>();
            _configurators.Add(configurator);
            return configurator;
        }

        public void Merge(EventTypeMap<TState> other)
        {
            foreach (var otherMapping in other)
            {
                if (_items.TryGetValue(otherMapping.EventType, out var existingMapping))
                {
                    existingMapping.EventApplier = otherMapping.EventApplier ?? existingMapping.EventApplier;
                    existingMapping.ConfigExtensions.AddRange(otherMapping.ConfigExtensions);
                }
                else
                {
                    _items.Add(otherMapping.EventType, otherMapping.ToBuilder());
                }
            }
        }

        public EventTypeMap<TState> Build()
        {
            foreach (var configurator in _configurators)
            {
                switch (configurator)
                {
                    case IEventTypeConfigurator c0:
                        c0.Configure(this);
                        break;
                    case IEventTypeConfigurator<TState> c1:
                        c1.Configure(this);
                        break;
                }
            }

            var items = _items.ToDictionary(x => x.Key, x => x.Value.Build());

            return new EventTypeMap<TState>(items);
        }

        internal void AddConfigurators(IEnumerable<IEventTypeConfigurator> configurators)
        {
            _configurators.AddRange(configurators);
        }

        internal void MapAll<TEventBase>(EventApplier<TState>? eventApplier, Func<Type, bool>? typeFilter)
        {
            typeFilter ??= _ => true;

            var eventTypes = typeof(TEventBase).FindAssignableConcreteTypes().Where(typeFilter);

            foreach (var eventType in eventTypes)
            {
                if (!_items.TryGetValue(eventType, out var mappingBuilder))
                {
                    mappingBuilder = new EventTypeMapping<TState>(eventType).ToBuilder();
                    _items.Add(eventType, mappingBuilder);
                }

                if (mappingBuilder.EventApplier == null && eventApplier != null)
                {
                    mappingBuilder.EventApplier = eventApplier;
                }
            }
        }

        internal void MapAll<TEventBase>(
            string eventApplierMethodName, bool isNonPublicAllowed, Func<Type, bool>? typeFilter)
        {
            typeFilter ??= _ => true;

            var eventApplierMethods = typeof(TState)
                .FindEventApplierMethods(eventApplierMethodName, isNonPublicAllowed)
                .Where(x => x.ArgType.IsAssignableTo(typeof(TEventBase)))
                .ToList();

            var immutableEventApplierMethods = typeof(TState)
                .FindImmutableEventApplierMethods(eventApplierMethodName, isNonPublicAllowed)
                .Where(x => x.ArgType.IsAssignableTo(typeof(TEventBase)))
                .ToList();

            if (eventApplierMethods.Count > 0 && immutableEventApplierMethods.Count > 0)
            {
                throw new InvalidOperationException(
                    "Mix of mutable and immutable event applier method signatures not supported.");
            }

            eventApplierMethods = eventApplierMethods.Count > 0 ? eventApplierMethods : immutableEventApplierMethods;

            var baseEventApplierMethods = eventApplierMethods
                .Where(x => x.ArgType.IsInterface || x.ArgType.IsAbstract)
                .ToList();

            var concreteEventApplierMethods = eventApplierMethods
                .Except(baseEventApplierMethods)
                .ToList();

            if (baseEventApplierMethods.Count > 1)
            {
                throw new InvalidOperationException("Multiple event base type applier methods not supported.");
            }

            // Add concrete event appliers first, so that any base applier can behave as a default.
            foreach (var (eventType, method) in concreteEventApplierMethods)
            {
                if (!_items.TryGetValue(eventType, out var mappingBuilder))
                {
                    mappingBuilder = EventTypeMapping.CreateBuilder<TState>(eventType);
                    _items.Add(eventType, mappingBuilder);
                }

                mappingBuilder.EventApplier ??= EventApplier.Create<TState>(method);
            }

            if (baseEventApplierMethods.Count == 1)
            {
                var (eventBaseType, method) = baseEventApplierMethods[0];
                var eventTypes = eventBaseType.FindAssignableConcreteTypes().Where(typeFilter);
                var eventApplier = EventApplier.Create<TState>(method);

                foreach (var eventType in eventTypes)
                {
                    if (!_items.TryGetValue(eventType, out var mappingBuilder))
                    {
                        mappingBuilder = new EventTypeMapping<TState>(eventType).ToBuilder();
                        _items.Add(eventType, mappingBuilder);
                    }

                    mappingBuilder.EventApplier ??= eventApplier;
                }
            }
        }

        internal void Map<TEvent>(EventApplier<TState>? eventApplier, IEnumerable<IConfigExtension> configExtensions)
        {
            if (!_items.TryGetValue(typeof(TEvent), out var mappingBuilder))
            {
                mappingBuilder = EventTypeMapping.CreateBuilder<TState, TEvent>();
                _items.Add(typeof(TEvent), mappingBuilder);
            }

            if (eventApplier != null)
            {
                mappingBuilder.EventApplier = eventApplier;
            }

            mappingBuilder.ConfigExtensions.AddRange(configExtensions);
        }
    }

    public IEnumerator<EventTypeMapping<TState>> GetEnumerator() => _items.Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.Values.GetEnumerator();
}