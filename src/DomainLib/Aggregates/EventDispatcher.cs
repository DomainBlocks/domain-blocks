using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainLib.Aggregates
{
    /// <summary>
    /// Dispatches domain events onto an aggregate and returns an updated aggregate state
    /// </summary>
    public sealed class EventDispatcher<TEventBase>
    {
        private readonly EventRoutes<TEventBase> _routes;

        internal EventDispatcher(EventRoutes<TEventBase> eventRoutes)
        {
            _routes = eventRoutes ?? throw new ArgumentNullException(nameof(eventRoutes));
        }

        public TAggregate DispatchEvents<TAggregate>(TAggregate aggregateRoot, IEnumerable<TEventBase> events)
        {
            return events.Aggregate(aggregateRoot, DispatchEvent);
        }

        public TAggregate DispatchEvents<TAggregate>(TAggregate aggregateRoot, params TEventBase[] events)
        {
            return events.Aggregate(aggregateRoot, DispatchEvent);
        }

        public TAggregate DispatchEvent<TAggregate>(TAggregate aggregateRoot, TEventBase @event)
        {
            var eventType = @event.GetType();
            var aggregateRootType = aggregateRoot.GetType();
            var aggregateAndEventTypes = (aggregateRootType, eventType);

            if (_routes.TryGetValue(aggregateAndEventTypes, out var applyEvent))
            {
                return (TAggregate)applyEvent(aggregateRoot, @event);
            }

            // If we get here, there is no explicit route specified for this event type.
            // Try and get a route to the event base type, i.e. a default route.
            if (_routes.TryGetValue((aggregateRootType, typeof(TEventBase)), out var defaultApplyEvent))
            {
                return (TAggregate)defaultApplyEvent(aggregateRoot, @event);
            }

            //// No default route specified.
            var message = $"No route or default route found when attempting to apply event " +
                          $"{eventType.Name} to {aggregateRootType.Name}";
            throw new InvalidOperationException(message);
        }
    }
}