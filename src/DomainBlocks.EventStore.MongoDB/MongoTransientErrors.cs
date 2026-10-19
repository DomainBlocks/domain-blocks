using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

internal static class MongoTransientErrors
{
    /// <summary>
    /// Treats a network error, a primary stepping down or shutting down, a lost cursor, and a timeout as transient,
    /// much as retryable reads do. Anything else, such as a failed authentication or a missing privilege, surfaces at
    /// once.
    /// </summary>
    public static bool IsTransient(Exception exception)
    {
        // See: https://github.com/mongodb/specifications/blob/master/source/retryable-reads/retryable-reads.md
        return exception switch
        {
            // An authentication failure is a connection exception too, but not a network one.
            MongoConnectionException { IsNetworkException: true } or MongoConnectionPoolPausedException => true,
            MongoNotPrimaryException or MongoNodeIsRecoveringException => true,
            MongoCursorNotFoundException => true,
            TimeoutException => true,
            MongoCommandException command => MongoErrorCodes.RetryableRead.Contains(command.Code),
            _ => false
        };
    }
}