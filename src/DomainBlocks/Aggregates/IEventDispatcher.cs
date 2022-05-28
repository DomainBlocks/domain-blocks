using System.Collections.Generic;

namespace DomainBlocks.Aggregates;

public interface IEventDispatcher
{
    // Can potentially dispatch by Type, or even event name here.
}

public interface IEventDispatcher<in TEventBase> : IEventDispatcher
{
    public TAggregate Dispatch<TAggregate>(TAggregate aggregateRoot, IEnumerable<TEventBase> events);
    public TAggregate Dispatch<TAggregate>(TAggregate aggregateRoot, params TEventBase[] events);
    TAggregate Dispatch<TAggregate>(TAggregate aggregateRoot, TEventBase @event);
}