using System.Net.Sockets;
using Npgsql;

namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

internal static class EventLogFeedResumePolicy
{
    /// <summary>
    /// Whether a feed failure is worth reconnecting for. Connection loss, server shutdown or restart, and resource
    /// exhaustion are transient; misconfiguration (missing publication, wrong wal_level, insufficient privileges) is
    /// not and should surface immediately.
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
