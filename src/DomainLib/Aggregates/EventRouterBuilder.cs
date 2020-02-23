using System;
using System.Collections;
using System.Collections.Generic;

namespace DomainLib.Aggregates
{
    /// <summary>
    /// Mutable class for building an EventRouter.
    /// </summary>
    public class EventRouterBuilder<TAggregateRoot, TDomainEventBase> :
        IEnumerable<KeyValuePair<Type, ApplyEvent<TAggregateRoot, TDomainEventBase>>>
    {
        private readonly List<KeyValuePair<Type, ApplyEvent<TAggregateRoot, TDomainEventBase>>> _routes =
            new List<KeyValuePair<Type, ApplyEvent<TAggregateRoot, TDomainEventBase>>>();

        public void Add<TDomainEvent>(Func<TAggregateRoot, TDomainEvent, TAggregateRoot> applier)
            where TDomainEvent : TDomainEventBase
        {
            var route = KeyValuePair.Create<Type, ApplyEvent<TAggregateRoot, TDomainEventBase>>(
                typeof(TDomainEvent), (agg, e) => applier(agg, (TDomainEvent) e));

            _routes.Add(route);
        }

        public EventRouter<TAggregateRoot, TDomainEventBase> Build()
        {
            return new EventRouter<TAggregateRoot, TDomainEventBase>(this);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public IEnumerator<KeyValuePair<Type, ApplyEvent<TAggregateRoot, TDomainEventBase>>> GetEnumerator()
        {
            return _routes.GetEnumerator();
        }
    }
}
