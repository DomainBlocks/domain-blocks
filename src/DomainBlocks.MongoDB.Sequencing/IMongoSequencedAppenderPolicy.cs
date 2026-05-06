namespace DomainBlocks.MongoDB.Sequencing;

/// <summary>
/// Defines a policy for pre-commit logic and conflict resolution for a
/// <see cref="MongoSequencedAppender{TDocument,TContext}"/>.
/// </summary>
public interface IMongoSequencedAppenderPolicy<TContext>
{
    /// <summary>
    /// Called before each batch is committed. Implementations may inspect and mutate the documents in each append
    /// entry, and chose to succeed or fail individual entries early. Entries not completed here will be committed.
    /// </summary>
    ValueTask OnBatchCommittingAsync(IReadOnlyList<AppendEntry<TContext>> batch, CancellationToken cancellationToken);

    /// <summary>
    /// Called when a duplicate key conflict is detected during a commit attempt. Return
    /// <see cref="ConflictResolution.Retry"/> to retry the append operation, or <see cref="ConflictResolution.Fail"/>
    /// to fault it.
    /// </summary>
    ConflictResolution OnConflict(ConflictingAppendEntry<TContext> conflict);
}