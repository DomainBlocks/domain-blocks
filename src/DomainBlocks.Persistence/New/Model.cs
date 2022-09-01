using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Persistence.New;

public class Model
{
    // Second type is the type of the event - but we don't need this as part of the key, as we'll never use a different
    // type of event for a given aggregate type. This is an interesting idea, as it means we can hide the event type in
    // the aggregate repository.
    private readonly IReadOnlyDictionary<(Type, Type), IAggregateType> _aggregateConfigs;

    public Model(IEnumerable<IAggregateType> aggregateTypes)
    {
        _aggregateConfigs = aggregateTypes.ToDictionary(x => x.Key);
    }

    public AggregateType<TAggregate, TEventBase> GetAggregateType<TAggregate, TEventBase>()
    {
        var key = (typeof(TAggregate), typeof(TEventBase));
        return (AggregateType<TAggregate, TEventBase>)_aggregateConfigs[key];
    }
}