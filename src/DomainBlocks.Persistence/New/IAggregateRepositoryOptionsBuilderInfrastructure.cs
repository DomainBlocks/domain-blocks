using System;
using DomainBlocks.Core;

namespace DomainBlocks.Persistence.New;

public interface IAggregateRepositoryOptionsBuilderInfrastructure
{
    void WithAggregateRepositoryFactory(Func<Model, IAggregateRepository> factory);
    void WithSnapshotRepositoryFactory(Func<IEventNameMap, ISnapshotRepository> factory);
}