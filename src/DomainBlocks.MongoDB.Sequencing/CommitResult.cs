namespace DomainBlocks.MongoDB.Sequencing;

/// <summary>
/// Represents the outcome of committing a batch's documents.
/// </summary>
internal abstract class CommitResult
{
    private CommitResult()
    {
    }

    public sealed class Success : CommitResult;

    /// <summary>
    /// Represents a commit that a duplicate key aborted, so that none of the documents were committed.
    /// </summary>
    public sealed class Conflict(int documentIndex, string? errorMessage, Exception originatingException) :
        CommitResult
    {
        /// <summary>
        /// Gets the index of the conflicting document among the documents committed.
        /// </summary>
        public int DocumentIndex { get; } = documentIndex;

        public string? ErrorMessage { get; } = errorMessage;

        public Exception OriginatingException { get; } = originatingException;
    }
}