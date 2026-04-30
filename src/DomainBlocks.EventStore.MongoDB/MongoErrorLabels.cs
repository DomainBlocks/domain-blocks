namespace DomainBlocks.EventStore.MongoDB;

internal static class MongoErrorLabels
{
    public const string ResumableChangeStreamError = "ResumableChangeStreamError";
    public const string TransientTransactionError = "TransientTransactionError";
    public const string UnknownTransactionCommitResult = "UnknownTransactionCommitResult";
}