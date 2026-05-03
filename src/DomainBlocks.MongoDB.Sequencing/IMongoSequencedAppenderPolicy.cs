namespace DomainBlocks.MongoDB.Sequencing;

public interface IMongoSequencedAppenderPolicy<TContext>
{
    ValueTask OnBatchCommittingAsync(IReadOnlyList<AppendEntry<TContext>> batch, CancellationToken cancellationToken);

    void OnConflict(AppendEntry<TContext> conflict);
}