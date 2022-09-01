using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Persistence.New.Builders;

public class ModelBuilder
{
    private readonly List<IAggregateTypeBuilder> _aggregateTypeBuilders = new();

    internal void AddAggregateConfigurationBuilder(IAggregateTypeBuilder builder)
    {
        _aggregateTypeBuilders.Add(builder);
    }

    public AggregateTypeBuilder<TAggregate> Aggregate<TAggregate>()
    {
        var builder = new AggregateTypeBuilder<TAggregate>(this);
        return builder;
    }

    public Model Build()
    {
        var aggregateConfigs = _aggregateTypeBuilders.Select(x => x.Build());
        return new Model(aggregateConfigs);
    }
}