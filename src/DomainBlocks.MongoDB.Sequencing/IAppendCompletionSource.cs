namespace DomainBlocks.MongoDB.Sequencing;

/// <summary>
/// Controls the completion of <see cref="AppendEntry{TContext}"/> instances.
/// </summary>
public interface IAppendCompletionSource<TContext>
{
    /// <summary>
    /// Attempts to complete the append entry, optionally with an error.
    /// </summary>
    /// <param name="appendEntry">The entry to complete.</param>
    /// <param name="error">If provided, the entry is completed with this exception as the failure cause.</param>
    /// <returns>
    /// <c>true</c> if the entry was completed; <c>false</c> if it was already completed.
    /// </returns>
    bool TryComplete(AppendEntry<TContext> appendEntry, Exception? error = null);
}