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
    public class EventRouter<TAggregateRoot, TDomainEventBase>
    {
        private readonly IReadOnlyDictionary<Type, ApplyEvent<TAggregateRoot, TDomainEventBase>> _routes;

        public EventRouter() : this(ImmutableDictionary<Type, ApplyEvent<TAggregateRoot, TDomainEventBase>>.Empty)
        {
        }

        public EventRouter(IEnumerable<KeyValuePair<Type, ApplyEvent<TAggregateRoot, TDomainEventBase>>> routes)
        {
            _routes = ImmutableDictionary.CreateRange(routes);
        }
        
        public TAggregateRoot Apply(TAggregateRoot aggregate, TDomainEventBase @event)
        {
            var key = @event.GetType();
            return _routes.TryGetValue(key, out var route) ? route(aggregate, @event) : aggregate;
        }

        public TAggregateRoot Apply(TAggregateRoot aggregate, IEnumerable<TDomainEventBase> events)
        {
            return events.Aggregate(aggregate, Apply);
        }
    }
}
