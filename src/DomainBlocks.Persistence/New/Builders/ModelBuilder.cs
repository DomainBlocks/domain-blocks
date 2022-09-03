using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Persistence.New.Builders;

public class ModelBuilder
{
    private readonly List<IAggregateTypeBuilder> _aggregateTypeBuilders = new();

    public ModelBuilder Aggregate<TAggregate, TEventBase>(
        Action<MutableAggregateTypeBuilder<TAggregate, TEventBase>> builderAction) where TEventBase : class
    {
        var builder = new MutableAggregateTypeBuilder<TAggregate, TEventBase>();
        _aggregateTypeBuilders.Add(builder);
        builderAction(builder);
        return this;
    }
    
    public ModelBuilder ImmutableAggregate<TAggregate, TEventBase>(
        Action<ImmutableAggregateTypeBuilder<TAggregate, TEventBase>> builderAction) where TEventBase : class
    {
        var builder = new ImmutableAggregateTypeBuilder<TAggregate, TEventBase>();
        _aggregateTypeBuilders.Add(builder);
        builderAction(builder);
        return this;
    }

    public Model Build()
    {
        var aggregateTypes = _aggregateTypeBuilders.Select(x => x.Build());
        return new Model(aggregateTypes);
    }
}