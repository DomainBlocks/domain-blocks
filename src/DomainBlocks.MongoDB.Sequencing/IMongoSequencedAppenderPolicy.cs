namespace DomainBlocks.MongoDB.Sequencing;

/// <summary>
/// Defines a policy for pre-commit logic and conflict resolution for a
/// <see cref="MongoSequencedAppender{TDocument,TContext}"/>.
/// </summary>
public interface IMongoSequencedAppenderPolicy<TContext>
{
    /// <summary>
    /// Called before a batch is committed. Implementations may inspect and mutate the documents in each append
    /// request, and choose to succeed or fail individual requests early. Requests not completed here will be committed.
    /// </summary>
    /// <remarks>
    /// This method is called:
    /// <list type="bullet">
    /// <item><description>Before the first commit attempt.</description></item>
    /// <item><description>After a duplicate key conflict, with the requests still pending.</description></item>
    /// <item><description>After a transient error, if queued requests have joined the batch.</description></item>
    /// </list>
    /// Requests queued during a commit attempt may join the batch when a transient error, such as a write conflict,
    /// aborts the attempt.
    /// </remarks>
    Task OnBatchCommittingAsync(IReadOnlyList<AppendRequest<TContext>> batch, CancellationToken cancellationToken);

    /// <summary>
    /// Called when a duplicate key conflict is detected during a commit attempt. Return
    /// <see cref="ConflictResolution.Retry"/> to retry the append operation, <see cref="ConflictResolution.Succeed"/>
    /// to complete it without writing its documents, or <see cref="ConflictResolution.Fail"/> to fault it.
    /// </summary>
    ValueTask<ConflictResolution> OnConflictAsync(
        AppendConflict<TContext> conflict,
        CancellationToken cancellationToken);
}