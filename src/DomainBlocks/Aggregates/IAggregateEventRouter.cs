using System.Collections.Generic;

namespace DomainBlocks.Aggregates;

public interface IAggregateEventRouter
{
}

public interface IAggregateEventRouter<in TEventBase> : IAggregateEventRouter
{
    public TAggregate Send<TAggregate>(TAggregate aggregateRoot, IEnumerable<TEventBase> events);
    public TAggregate Send<TAggregate>(TAggregate aggregateRoot, params TEventBase[] events);
    TAggregate Send<TAggregate>(TAggregate aggregateRoot, TEventBase @event);
}