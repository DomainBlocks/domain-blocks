using Npgsql;

namespace DomainBlocks.EventStore.PostgreSQL;

internal static class PostgresTransientErrors
{
    /// <summary>
    /// Treats as transient an unconfirmed commit, a server in recovery, and the errors that Npgsql counts as transient.
    /// </summary>
    public static bool IsTransient(Exception exception) =>
        exception is CommitNotConfirmedException or ServerInRecoveryException or NpgsqlException { IsTransient: true };
}