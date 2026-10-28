using System.Net.Sockets;
using Npgsql;

namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

internal static class EventLogFeedResumePolicy
{
    /// <summary>
    /// Treats connection loss, server restarts, resource exhaustion, a replication slot that is lost or still in use,
    /// and a server in recovery as transient. Misconfiguration, such as a missing publication, the wrong wal_level, or
    /// missing privileges, is not transient, so it surfaces immediately.
    /// </summary>
    public static bool IsTransient(Exception exception)
    {
        return exception switch
        {
            ReplicationSlotLostException => true,
            ReplicationSlotInUseException => true,
            ServerInRecoveryException => true,

            // Npgsql counts this error as transient. An invalidated slot raises it too, but the session reports that as
            // ReplicationSlotLostException. What remains is misconfiguration, such as the wrong wal_level, which
            // retrying can't fix.
            PostgresException { SqlState: PostgresErrorCodes.ObjectNotInPrerequisiteState } => false,

            PostgresException { SqlState: PostgresErrorCodes.AdminShutdown } => true,
            PostgresException { SqlState: PostgresErrorCodes.CrashShutdown } => true,
            PostgresException { SqlState: PostgresErrorCodes.CannotConnectNow } => true,
            PostgresException { SqlState: PostgresErrorCodes.TooManyConnections } => true,
            PostgresException { SqlState: PostgresErrorCodes.ConfigurationLimitExceeded } => true,
            NpgsqlException npgsqlException => npgsqlException.IsTransient ||
                                               npgsqlException.InnerException is IOException or SocketException,
            IOException => true,
            SocketException => true,
            TimeoutException => true,
            OperationCanceledException => true, // A timeout-style cancellation from the driver, not our own stop.
            _ => false
        };
    }
}