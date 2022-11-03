using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Core;

public class Model
{
    private readonly IReadOnlyDictionary<Type, IAggregateOptions> _aggregateOptions;
    private readonly EventNameMap _eventNameMap;

    public Model(IEnumerable<IAggregateOptions> aggregateOptions)
    {
        _aggregateOptions = aggregateOptions.ToDictionary(x => x.ClrType);

        // Build event name map from all aggregate options.
        var allEventOptions = _aggregateOptions.Values.SelectMany(x => x.EventOptions);
        _eventNameMap = new EventNameMap();
        foreach (var eventType in allEventOptions)
        {
            _eventNameMap.Add(eventType.EventName, eventType.ClrType);
        }
    }

    public IEventNameMap EventNameMap => _eventNameMap;
    
    public IAggregateOptions<TAggregate> GetAggregateOptions<TAggregate>()
    {
        return (IAggregateOptions<TAggregate>)_aggregateOptions[typeof(TAggregate)];
    }
}