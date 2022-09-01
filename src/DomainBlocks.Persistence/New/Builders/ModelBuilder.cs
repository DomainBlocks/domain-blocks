using System;
using System.Collections.Generic;
using System.Linq;
using DomainBlocks.Aggregates;

namespace DomainBlocks.Persistence.New.Builders;

public class ModelBuilder
{
    private readonly List<IAggregateTypeBuilder> _aggregateTypeBuilders = new();

    public ModelBuilder Aggregate<TAggregate, TEventBase>(
        Action<AggregateTypeBuilder<TAggregate, TEventBase>> builderAction)
    {
        var builder = new AggregateTypeBuilder<TAggregate, TEventBase>();
        _aggregateTypeBuilders.Add(builder);
        builderAction(builder);
        return this;
    }

    public Model Build()
    {
        var aggregateTypes = _aggregateTypeBuilders.Select(x => x.Build()).ToList();
        var allEventTypes = aggregateTypes.SelectMany(x => x.EventTypes);

        var eventNameMap = new EventNameMap();
        foreach (var eventType in allEventTypes)
        {
            eventNameMap.Add(eventType.EventName, eventType.ClrType);
        }

        return new Model(aggregateTypes, eventNameMap);
    }
}