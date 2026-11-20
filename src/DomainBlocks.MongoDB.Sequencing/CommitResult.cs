namespace DomainBlocks.MongoDB.Sequencing;

internal abstract class CommitResult
{
    private CommitResult()
    {
    }

    public sealed class Success : CommitResult;

    public sealed class TransientError : CommitResult;

    public sealed class Conflict(int documentIndex, string? errorMessage, Exception originatingException) :
        CommitResult
    {
        public int DocumentIndex { get; } = documentIndex;

        public string? ErrorMessage { get; } = errorMessage;

        public Exception OriginatingException { get; } = originatingException;
    }
}