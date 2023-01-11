namespace DomainBlocks.Core.Persistence;

public class AggregateRepositoryOptionsBuilder : IAggregateRepositoryOptionsBuilderInfrastructure
{
    public AggregateRepositoryOptions Options { get; private set; } = new();

    void IAggregateRepositoryOptionsBuilderInfrastructure.WithAggregateRepositoryFactory(
        Func<Model, IAggregateRepository> factory)
    {
        Options = Options.WithAggregateRepositoryFactory(factory);
    }

    void IAggregateRepositoryOptionsBuilderInfrastructure.WithSnapshotRepositoryFactory(
        Func<Model, ISnapshotRepository> factory)
    {
        Options = Options.WithSnapshotRepositoryFactory(factory);
    }
}