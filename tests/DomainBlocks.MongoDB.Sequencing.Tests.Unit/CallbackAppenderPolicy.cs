namespace DomainBlocks.MongoDB.Sequencing.Tests.Unit;

/// <summary>
/// Represents a policy that passes each call to a test's callback. Without a callback, it does nothing before a commit
/// and fails each conflict.
/// </summary>
internal sealed class CallbackAppenderPolicy<TContext>(
    Func<IReadOnlyList<AppendRequest<TContext>>, CancellationToken, Task>? onBatchCommitting = null,
    Func<AppendConflict<TContext>, ConflictResolution>? onConflict = null) :
    IMongoSequencedAppenderPolicy<TContext>
{
    public Task OnBatchCommittingAsync(IReadOnlyList<AppendRequest<TContext>> batch, CancellationToken ct)
    {
        return onBatchCommitting?.Invoke(batch, ct) ?? Task.CompletedTask;
    }

    public ConflictResolution OnConflict(AppendConflict<TContext> conflict)
    {
        return onConflict?.Invoke(conflict) ?? ConflictResolution.Fail();
    }
}