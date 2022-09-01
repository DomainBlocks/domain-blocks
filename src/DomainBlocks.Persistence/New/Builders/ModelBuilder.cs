using System;
using System.Collections.Generic;
using System.Linq;

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
        var aggregateConfigs = _aggregateTypeBuilders.Select(x => x.Build());
        return new Model(aggregateConfigs);
    }
}