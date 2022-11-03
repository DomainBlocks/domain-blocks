using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Core.Builders;

public class ModelBuilder
{
    private readonly List<IAggregateOptionsBuilder> _aggregateOptionsBuilders = new();

    public ModelBuilder Aggregate<TAggregate, TEventBase>(
        Action<MutableAggregateOptionsBuilder<TAggregate, TEventBase>> builderAction) where TEventBase : class
    {
        if (builderAction == null) throw new ArgumentNullException(nameof(builderAction));

        var builder = new MutableAggregateOptionsBuilder<TAggregate, TEventBase>();
        _aggregateOptionsBuilders.Add(builder);
        builderAction(builder);
        return this;
    }

    public ModelBuilder ImmutableAggregate<TAggregate, TEventBase>(
        Action<ImmutableAggregateOptionsBuilder<TAggregate, TEventBase>> builderAction) where TEventBase : class
    {
        if (builderAction == null) throw new ArgumentNullException(nameof(builderAction));

        var builder = new ImmutableAggregateOptionsBuilder<TAggregate, TEventBase>();
        _aggregateOptionsBuilders.Add(builder);
        builderAction(builder);
        return this;
    }

    public Model Build()
    {
        var aggregateOptions = _aggregateOptionsBuilders.Select(x => x.Options);
        return new Model(aggregateOptions);
    }
}