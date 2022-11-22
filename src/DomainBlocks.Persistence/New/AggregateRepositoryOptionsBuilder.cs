using System;
using DomainBlocks.Core;

namespace DomainBlocks.Persistence.New;

public class AggregateRepositoryOptionsBuilder : IAggregateRepositoryOptionsBuilderInfrastructure
{
    public AggregateRepositoryOptions Options { get; private set; } = new();

    void IAggregateRepositoryOptionsBuilderInfrastructure.WithAggregateRepositoryFactory(
        Func<Model, IAggregateRepository> factory)
    {
        Options = Options.WithAggregateRepositoryFactory(factory);
    }

    void IAggregateRepositoryOptionsBuilderInfrastructure.WithSnapshotRepositoryFactory(
        Func<IEventNameMap, ISnapshotRepository> factory)
    {
        Options = Options.WithSnapshotRepositoryFactory(factory);
    }
}