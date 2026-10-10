using System.Net.Sockets;
using Npgsql;

namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

internal static class EventLogFeedResumePolicy
{
    /// <summary>
    /// Treats connection loss, server restarts, and resource exhaustion as transient. Misconfiguration, such as a
    /// missing publication, the wrong wal_level, or missing privileges, is not transient, so it surfaces immediately.
    /// </summary>
    public static bool IsTransient(Exception exception)
    {
        return exception switch
        {
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