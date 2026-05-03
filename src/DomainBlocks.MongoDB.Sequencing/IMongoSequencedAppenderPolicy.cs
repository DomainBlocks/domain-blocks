namespace DomainBlocks.MongoDB.Sequencing;

public interface IMongoSequencedAppenderPolicy<TContext>
{
    ValueTask OnCommittingAsync(IReadOnlyList<AppendEntry<TContext>> batch, CancellationToken cancellationToken);

    void OnConflict(AppendEntry<TContext> conflict);
}