using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace DomainLib.Aggregates
{
    /// <summary>
    /// Immutable class that routes events to apply methods on an aggregate root.
    /// </summary>
    /// <typeparam name="TAggregateRoot">The type of the aggregate root to apply events too.</typeparam>
    /// <typeparam name="TDomainEventBase">The base type of all events that are applied.</typeparam>
    public class ApplyEventRouter<TAggregateRoot, TDomainEventBase>
    {
        private readonly IReadOnlyDictionary<Type, ApplyEvent<TAggregateRoot, TDomainEventBase>> _routes;

        public ApplyEventRouter(IEnumerable<KeyValuePair<Type, ApplyEvent<TAggregateRoot, TDomainEventBase>>> routes)
        {
            _routes = ImmutableDictionary.CreateRange(routes);
        }

        public TAggregateRoot Route(TAggregateRoot aggregateRoot, TDomainEventBase @event)
        {
            var eventType = @event.GetType();
            if (_routes.TryGetValue(eventType, out var applyEvent))
            {
                return applyEvent(aggregateRoot, @event);
            }

            // If we get here, there is no explicit route specified for this event type.
            // Try and get a route to the event base type, i.e. a default route.
            if (_routes.TryGetValue(typeof(TDomainEventBase), out var defaultApplyEvent))
            {
                return defaultApplyEvent(aggregateRoot, @event);
            }
            
            // No default route specified.
            var message = $"No route or default route found when attempting to apply " +
                          $"{eventType.Name} to {aggregateRoot.GetType().Name}";
            throw new InvalidOperationException(message);
        }

        public TAggregateRoot Route(TAggregateRoot aggregateRoot, IEnumerable<TDomainEventBase> events)
        {
            return events.Aggregate(aggregateRoot, Route);
        }
        
        public TAggregateRoot Route(TAggregateRoot aggregateRoot, params TDomainEventBase[] events)
        {
            return events.Aggregate(aggregateRoot, Route);
        }
    }
}