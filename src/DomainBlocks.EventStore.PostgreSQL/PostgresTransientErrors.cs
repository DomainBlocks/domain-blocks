using Npgsql;

namespace DomainBlocks.EventStore.PostgreSQL;

internal static class PostgresTransientErrors
{
    /// <summary>
    /// Treats an unconfirmed replication barrier, a server in recovery, and the errors that Npgsql counts as transient
    /// as transient. Npgsql counts a lost connection, a timeout, and a server that is shutting down, starting up, or
    /// out of connections. Any other error, such as a missing table or a missing privilege, is not transient.
    /// </summary>
    public static bool IsTransient(Exception exception) =>
        exception is ReplicationBarrierException or ServerInRecoveryException or NpgsqlException { IsTransient: true };
}