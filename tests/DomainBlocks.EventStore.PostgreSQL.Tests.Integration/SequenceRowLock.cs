using Npgsql;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

/// <summary>
/// Locks the sequence row in a test transaction. Every append batch locks the row too, so a batch waits inside the
/// database until the lock is released.
/// </summary>
internal sealed class SequenceRowLock(
    NpgsqlDataSource dataSource,
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    CancellationTokenSource cancellation,
    Task locking) :
    IAsyncDisposable
{
    // Returns once the test transaction holds the lock.
    public static async Task<SequenceRowLock> AcquireAsync(
        NpgsqlDataSource dataSource,
        string schema,
        CancellationToken ct)
    {
        var rowLock = await RequestAsync(dataSource, schema, ct);
        await rowLock.WaitUntilLockedAsync();
        return rowLock;
    }

    // Returns once the test transaction waits for the lock, so it takes the lock before any later request does.
    public static async Task<SequenceRowLock> QueueAsync(
        NpgsqlDataSource dataSource,
        string schema,
        CancellationToken ct)
    {
        var rowLock = await RequestAsync(dataSource, schema, ct);
        await rowLock.PollAsync("SELECT cardinality(pg_blocking_pids($1)) > 0", ct);
        return rowLock;
    }

    // Waits until another backend waits for the lock.
    public Task WaitUntilBlockingAsync(CancellationToken ct)
    {
        return PollAsync("SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE $1 = ANY (pg_blocking_pids(pid)))", ct);
    }

    public async Task ReleaseAsync(CancellationToken ct)
    {
        await WaitUntilLockedAsync();
        await transaction.RollbackAsync(ct);
    }

    // Cancels a request that still waits, as when a test fails first, since closing the connection mid-command would
    // throw.
    public async ValueTask DisposeAsync()
    {
        await cancellation.CancelAsync();
        await locking.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        cancellation.Dispose();
        await transaction.DisposeAsync();
        await connection.DisposeAsync();
    }

    private static async Task<SequenceRowLock> RequestAsync(
        NpgsqlDataSource dataSource,
        string schema,
        CancellationToken ct)
    {
        var connection = await dataSource.OpenConnectionAsync(ct);
        var transaction = await connection.BeginTransactionAsync(ct);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var locking = LockAsync(connection, schema, cancellation.Token);

        return new SequenceRowLock(dataSource, connection, transaction, cancellation, locking);
    }

    private Task WaitUntilLockedAsync() => locking;

    private static async Task LockAsync(NpgsqlConnection connection, string schema, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"SELECT next FROM {schema}.sequences FOR UPDATE", connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    // Polls outside the test transaction, which would keep seeing the activity snapshot it took first.
    private async Task PollAsync(string sql, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = connection.ProcessID });

        while (!(bool)(await command.ExecuteScalarAsync(ct)).ShouldNotBeNull())
            await Task.Delay(20, ct);
    }
}