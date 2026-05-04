namespace DomainBlocks.MongoDB.Sequencing;

/// <summary>
/// The default policy that performs no pre-commit logic and always retries on conflict. Used when no custom policy is
/// provided.
/// </summary>
public sealed class DefaultSequencedAppenderPolicy<TContext> : IMongoSequencedAppenderPolicy<TContext>
{
    public static readonly DefaultSequencedAppenderPolicy<TContext> Shared = new();

    private DefaultSequencedAppenderPolicy()
    {
    }

    public ValueTask OnBatchCommittingAsync(
        IReadOnlyList<AppendEntry<TContext>> batch,
        IAppendCompletionSource<TContext> completionSource,
        CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    public ConflictResolution OnConflict(AppendEntry<TContext> conflictingAppend, AppendConflictInfo conflictInfo)
    {
        return ConflictResolution.Retry;
    }
}