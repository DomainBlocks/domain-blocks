using Npgsql;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// Watches a connection for the warning that a commit completed without confirmation from the synchronous standbys.
/// </summary>
internal sealed class CommitConfirmationWatcher : IDisposable
{
    private const string SyncRepWaitRoutine = "SyncRepWaitForLSN";

    private readonly NpgsqlConnection _connection;
    private PostgresNotice? _warning;

    public CommitConfirmationWatcher(NpgsqlConnection connection)
    {
        _connection = connection;
        _connection.Notice += OnNotice;
    }

    // A commit whose wait for the synchronous standbys is canceled completes normally, with only a warning. Its code
    // is generic and its text translated, so it is recognized by the server function that raises it, which notices
    // report untranslated and which has kept its name since PostgreSQL 9.1. A warning that names no function counts
    // too, so that a server that stopped naming it would leave commits unconfirmed.
    public static bool IsUnconfirmedCommitWarning(PostgresNotice notice) =>
        notice.InvariantSeverity == "WARNING" &&
        (string.IsNullOrEmpty(notice.Routine) || notice.Routine == SyncRepWaitRoutine);

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