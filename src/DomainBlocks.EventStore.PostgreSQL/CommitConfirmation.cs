using Npgsql;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// Confirms a commit that sent a warning, which may mean that its wait for the synchronous standbys was canceled.
/// </summary>
/// <remarks>
/// <para>
/// A canceled wait sends only a warning, and the commit stands on the primary. Its code is the generic warning code and
/// its text is translated, so the warning cannot be told apart from others, such as one raised by a trigger. Instead,
/// any warning is followed by a barrier on the same connection: a commit of our own that writes WAL with
/// <c>synchronous_commit</c> on and touches no table, so that no trigger runs. If it commits without a warning, the
/// standbys have flushed it, and with it every commit before it, because they receive WAL in order.
/// </para>
/// <para>
/// The barrier runs on the same connection, so it can only confirm the server that ran the commit. A failover in
/// between makes it fail, rather than confirm on a new primary that lacks the commit. Without synchronous standbys it
/// writes nothing, because a commit is then flushed before it returns.
/// </para>
/// </remarks>
internal static class CommitConfirmation
{
    private const string BarrierSql =
        "SELECT set_config('synchronous_commit', 'on', true), pg_logical_emit_message(true, 'dbx_confirm', '') " +
        "WHERE current_setting('synchronous_standby_names') <> ''";

    /// <summary>
    /// Confirms the commit that sent the warning, or throws <see cref="CommitNotConfirmedException"/>.
    /// </summary>
    /// <param name="connection">The connection that the commit ran on.</param>
    /// <param name="warning">The warning that the commit sent.</param>
    /// <param name="committed">What committed, to describe it in the exception.</param>
    /// <param name="cancellationToken">The token to cancel the barrier with.</param>
    public static async Task ConfirmAsync(
        NpgsqlConnection connection,
        PostgresNotice warning,
        string committed,
        CancellationToken cancellationToken)
    {
        PostgresNotice? barrierWarning = null;
        connection.Notice += OnNotice;

        try
        {
            await using var command = new NpgsqlCommand(BarrierSql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            connection.Notice -= OnNotice;
        }

        if (barrierWarning is not null)
        {
            throw new CommitNotConfirmedException(
                $"{committed} committed on the primary, but its commit was not confirmed: {warning.MessageText}");
        }

        return;

        void OnNotice(object? sender, NpgsqlNoticeEventArgs e)
        {
            if (IsWarning(e.Notice))
                barrierWarning ??= e.Notice;
        }
    }

    public static bool IsWarning(PostgresNotice notice) => notice.InvariantSeverity == "WARNING";
}