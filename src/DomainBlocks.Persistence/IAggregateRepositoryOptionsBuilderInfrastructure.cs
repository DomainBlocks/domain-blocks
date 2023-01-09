using System;
using DomainBlocks.Core;

namespace DomainBlocks.Persistence;

public interface IAggregateRepositoryOptionsBuilderInfrastructure
{
    void WithAggregateRepositoryFactory(Func<Model, IAggregateRepository> factory);
    void WithSnapshotRepositoryFactory(Func<Model, ISnapshotRepository> factory);
}