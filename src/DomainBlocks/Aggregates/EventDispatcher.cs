using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Aggregates
{
    /// <summary>
    /// Dispatches domain events onto an aggregate and returns an updated aggregate state
    /// </summary>
    public sealed class EventDispatcher<TEventBase>
    {
        private readonly EventRoutes<TEventBase> _routes;
        private readonly ImmutableEventRoutes<TEventBase> _immutableRoutes;

        internal EventDispatcher(EventRoutes<TEventBase> eventRoutes, ImmutableEventRoutes<TEventBase> immutableEventRoutes)
        {
            _routes = eventRoutes ?? throw new ArgumentNullException(nameof(eventRoutes));
            _immutableRoutes = immutableEventRoutes ?? throw new ArgumentNullException(nameof(immutableEventRoutes));
        }
        
        public void Dispatch<TAggregate>(TAggregate aggregateRoot, IEnumerable<TEventBase> events)
        {
            foreach (var e in events)
            {
                Dispatch(aggregateRoot, e);
            }
        }

        public void Dispatch<TAggregate>(TAggregate aggregateRoot, params TEventBase[] events)
        {
            Dispatch(aggregateRoot, events.AsEnumerable());
        }

        public void Dispatch<TAggregate>(TAggregate aggregateRoot, TEventBase @event)
        {
            var eventType = @event.GetType();
            var aggregateRootType = aggregateRoot.GetType();
            var routeKey = (aggregateRootType, eventType);

            if (_routes.TryGetValue(routeKey, out var applyEvent))
            {
                applyEvent(aggregateRoot, @event);
                return;
            }

            // If we get here, there is no explicit route specified for this event type.
            // Try and get a route to the event base type, i.e. a default route.
            if (_routes.TryGetValue((aggregateRootType, typeof(TEventBase)), out var defaultApplyEvent))
            {
                defaultApplyEvent(aggregateRoot, @event);
                return;
            }

            // No default route specified.
            var message = $"No route or default route found when attempting to apply event " +
                          $"{eventType.Name} to {aggregateRootType.Name}";
            throw new InvalidOperationException(message);
        }

        public TAggregate ImmutableDispatch<TAggregate>(TAggregate aggregateRoot, IEnumerable<TEventBase> events)
        {
            return events.Aggregate(aggregateRoot, ImmutableDispatch);
        }

        public TAggregate ImmutableDispatch<TAggregate>(TAggregate aggregateRoot, params TEventBase[] events)
        {
            return events.Aggregate(aggregateRoot, ImmutableDispatch);
        }

        public TAggregate ImmutableDispatch<TAggregate>(TAggregate aggregateRoot, TEventBase @event)
        {
            var eventType = @event.GetType();
            var aggregateRootType = aggregateRoot.GetType();
            var routeKey = (aggregateRootType, eventType);

            if (_immutableRoutes.TryGetValue(routeKey, out var applyEvent))
            {
                return (TAggregate)applyEvent(aggregateRoot, @event);
            }

            // If we get here, there is no explicit route specified for this event type.
            // Try and get a route to the event base type, i.e. a default route.
            if (_immutableRoutes.TryGetValue((aggregateRootType, typeof(TEventBase)), out var defaultApplyEvent))
            {
                return (TAggregate)defaultApplyEvent(aggregateRoot, @event);
            }

            // No default route specified.
            var message = $"No route or default route found when attempting to apply event " +
                          $"{eventType.Name} to {aggregateRootType.Name}";
            throw new InvalidOperationException(message);
        }
    }
}