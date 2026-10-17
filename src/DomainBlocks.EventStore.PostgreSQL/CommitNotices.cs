using Npgsql;

namespace DomainBlocks.EventStore.PostgreSQL;

internal static class CommitNotices
{
    // The server function in which a commit waits for the synchronous standbys, which raises the warning when the wait
    // is cut short. It has had this name since synchronous replication was added in PostgreSQL 9.1.
    private const string SynchronousReplicationWait = "SyncRepWaitForLSN";

    /// <summary>
    /// Gets whether a notice says that a commit's wait for the synchronous standbys was cut short, so that the commit
    /// stands on the primary but the standbys may not have it.
    /// </summary>
    /// <remarks>
    /// The warning has the generic warning code and translated text, so it is recognized by the server function that
    /// raised it, which the server reports untranslated. A warning that reports no function at all counts too, so that
    /// a server that stops reporting it is treated as unconfirmed rather than confirmed.
    /// </remarks>
    public static bool IsUnconfirmedCommit(PostgresNotice notice)
    {
        return notice.InvariantSeverity == "WARNING" &&
               (string.IsNullOrEmpty(notice.Routine) || notice.Routine == SynchronousReplicationWait);
    }
}