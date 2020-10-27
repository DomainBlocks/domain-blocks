using System;
using System.Collections;
using System.Collections.Generic;

namespace DomainLib.Aggregates
{
    /// <summary>
    /// Mutable class for building an EventRouter.
    /// </summary>
    public class ApplyEventRouterBuilder<TAggregateRoot, TDomainEventBase> :
        IEnumerable<KeyValuePair<Type, ApplyEvent<TAggregateRoot, TDomainEventBase>>>
    {
        private readonly List<KeyValuePair<Type, ApplyEvent<TAggregateRoot, TDomainEventBase>>> _routes =
            new List<KeyValuePair<Type, ApplyEvent<TAggregateRoot, TDomainEventBase>>>();

        private readonly IEventNameMap _eventNameMap = new EventNameMap();

        public void Add<TDomainEvent>(Func<TAggregateRoot, TDomainEvent, TAggregateRoot> eventApplier)
            where TDomainEvent : TDomainEventBase
        {
            var route = KeyValuePair.Create<Type, ApplyEvent<TAggregateRoot, TDomainEventBase>>(
                typeof(TDomainEvent), (agg, e) => eventApplier(agg, (TDomainEvent) e));

            _routes.Add(route);
            _eventNameMap.RegisterEvent<TDomainEvent>();
        }

        public ApplyEventRouter<TAggregateRoot, TDomainEventBase> Build()
        {
            return new ApplyEventRouter<TAggregateRoot, TDomainEventBase>(this, _eventNameMap);
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