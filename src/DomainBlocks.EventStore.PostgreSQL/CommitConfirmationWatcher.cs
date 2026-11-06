using Npgsql;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// Represents a watch on a connection for the warning that a commit completed without confirmation from the
/// synchronous standbys.
/// </summary>
/// <remarks>
/// A commit whose wait for the synchronous standbys is canceled, by a cancel request, <c>pg_cancel_backend</c>, or a
/// command timeout, stands on the primary and completes normally with only a warning. The warning has the generic
/// warning code and translated text, so it is recognized by the server function that raised it, which every notice
/// reports untranslated. A warning that reports no function counts too, so that a server that stopped reporting it
/// would leave commits unconfirmed rather than confirmed.
/// </remarks>
internal sealed class CommitConfirmationWatcher : IDisposable
{
    // The server function in which a commit waits for the synchronous standbys. It has had this name since synchronous
    // replication was added in PostgreSQL 9.1.
    private const string SyncRepWaitRoutine = "SyncRepWaitForLSN";

    private readonly NpgsqlConnection _connection;
    private PostgresNotice? _warning;

    public CommitConfirmationWatcher(NpgsqlConnection connection)
    {
        _connection = connection;
        _connection.Notice += OnNotice;
    }

    /// <summary>
    /// Gets a value that indicates whether a notice is the warning that a commit completed without confirmation from
    /// the synchronous standbys.
    /// </summary>
    public static bool IsUnconfirmedCommitWarning(PostgresNotice notice) =>
        notice.InvariantSeverity == "WARNING" &&
        (string.IsNullOrEmpty(notice.Routine) || notice.Routine == SyncRepWaitRoutine);

    /// <summary>
    /// Throws <see cref="CommitNotConfirmedException"/> if the connection was sent the warning.
    /// </summary>
    /// <param name="subject">What committed, such as "The append batch", to start the exception's message.</param>
    public void ThrowIfNotConfirmed(string subject)
    {
        if (_warning is not null)
        {
            throw new CommitNotConfirmedException(
                $"{subject} completed without confirmation from the synchronous standbys: {_warning.MessageText}");
        }
    }

    public void Dispose() => _connection.Notice -= OnNotice;

    private void OnNotice(object? sender, NpgsqlNoticeEventArgs e)
    {
        if (IsUnconfirmedCommitWarning(e.Notice))
            _warning ??= e.Notice;
    }
}