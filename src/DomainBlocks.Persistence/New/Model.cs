using System;
using System.Collections.Generic;
using System.Linq;
using DomainBlocks.Aggregates;

namespace DomainBlocks.Persistence.New;

public class Model
{
    private readonly IReadOnlyDictionary<Type, IAggregateType> _aggregateTypes;
    private readonly EventNameMap _eventNameMap;

    public Model(IEnumerable<IAggregateType> aggregateTypes)
    {
        _aggregateTypes = aggregateTypes.ToDictionary(x => x.ClrType);

        // Build event name map from all aggregate types.
        var allEventTypes = _aggregateTypes.Values.SelectMany(x => x.EventTypes);
        _eventNameMap = new EventNameMap();
        foreach (var eventType in allEventTypes)
        {
            _eventNameMap.Add(eventType.EventName, eventType.ClrType);
        }
    }

    public IEventNameMap EventNameMap => _eventNameMap;

    public AggregateType<TAggregate, TEventBase> GetAggregateType<TAggregate, TEventBase>() where TEventBase : class
    {
        return (AggregateType<TAggregate, TEventBase>)_aggregateTypes[typeof(TAggregate)];
    }
    
    public IAggregateType<TAggregate> GetAggregateType<TAggregate>()
    {
        return (IAggregateType<TAggregate>)_aggregateTypes[typeof(TAggregate)];
    }
}