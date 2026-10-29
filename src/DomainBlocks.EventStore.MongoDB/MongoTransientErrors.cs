using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

internal static class MongoTransientErrors
{
    /// <summary>
    /// Treats a network error, a paused connection pool, a lost cursor, a timeout, and the server errors that retryable
    /// reads retry, such as a primary stepping down or shutting down, as transient. Any other error, such as a failed
    /// authentication or a missing privilege, is not transient.
    /// </summary>
    public static bool IsTransient(Exception exception)
    {
        return exception switch
        {
            // An authentication failure is a connection exception too, but not a network one.
            MongoConnectionException { IsNetworkException: true } or MongoConnectionPoolPausedException => true,
            MongoCursorNotFoundException => true,
            TimeoutException => true,

            // A primary that steps down or shuts down raises a subclass of this exception with one of these codes.
            MongoCommandException command => MongoErrorCodes.RetryableRead.Contains(command.Code),
            _ => false
        };
    }
}