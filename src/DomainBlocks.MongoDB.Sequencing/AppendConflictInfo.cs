namespace DomainBlocks.MongoDB.Sequencing;

/// <summary>
/// Provides details about a duplicate key conflict detected during a commit attempt.
/// </summary>
public sealed class AppendConflictInfo(int documentIndex, string? message, Exception originatingException)
{
    /// <summary>
    /// The index of the conflicting document within <see cref="AppendEntry{TContext}.Documents"/>.
    /// </summary>
    public int DocumentIndex { get; } = documentIndex;

    /// <summary>
    /// The error message provided by MongoDB, or <c>null</c> if unavailable.
    /// </summary>
    public string? Message { get; } = message;

    /// <summary>
    /// The exception that originally signaled the conflict, thrown by the MongoDB driver. Suitable for use as an inner
    /// exception.
    /// </summary>
    public Exception OriginatingException { get; } = originatingException;
}