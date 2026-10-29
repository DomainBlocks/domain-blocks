using System.Collections.Frozen;

namespace DomainBlocks.EventStore.MongoDB;

// See: https://github.com/mongodb/mongo/blob/r7.0.16/src/mongo/base/error_codes.yml
internal static class MongoErrorCodes
{
    public const int ChangeStreamHistoryLost = 286;

    /// <summary>
    /// The codes of the server errors that retryable reads retry, as the retryable reads specification lists them.
    /// </summary>
    /// <remarks>
    /// Each one means that the node was shutting down, stepping down or not primary, that a host or the network failed,
    /// that an operation ran out of time, or that a majority read concern was not available yet. None of them says
    /// that the read itself is wrong, so the same read can succeed once the replica set has a primary again. See
    /// <see href="https://github.com/mongodb/specifications/blob/master/source/retryable-reads/retryable-reads.md"/>.
    /// </remarks>
    public static readonly FrozenSet<int> RetryableRead = FrozenSet.Create(
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
    );
}