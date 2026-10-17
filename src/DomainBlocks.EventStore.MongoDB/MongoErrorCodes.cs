namespace DomainBlocks.EventStore.MongoDB;

// See: https://github.com/mongodb/mongo/blob/r7.0.16/src/mongo/base/error_codes.yml
internal static class MongoErrorCodes
{
    /// <summary>
    /// The codes of the errors that retryable reads retry: a node shutting down, stepping down or not being primary, a
    /// host or network failure, and a majority read concern that is not available yet.
    /// </summary>
    public static readonly IReadOnlySet<int> RetryableRead = new HashSet<int>
    {
        6, // HostUnreachable
        7, // HostNotFound
        89, // NetworkTimeout
        91, // ShutdownInProgress
        134, // ReadConcernMajorityNotAvailableYet
        189, // PrimarySteppedDown
        262, // ExceededTimeLimit
        9001, // SocketException
        10107, // NotWritablePrimary
        11600, // InterruptedAtShutdown
        11602, // InterruptedDueToReplStateChange
        13435, // NotPrimaryNoSecondaryOk
        13436 // NotPrimaryOrSecondary
    };
}