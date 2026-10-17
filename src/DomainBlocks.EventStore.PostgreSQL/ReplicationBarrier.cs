using Microsoft.Extensions.Logging;
using Npgsql;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// Runs a read with the replication barrier after it, in one round trip, so that subscriptions are only given events
/// that a failover cannot undo.
/// </summary>
/// <remarks>
/// <para>
/// The read and the barrier run as one batch, which is one implicit transaction. The barrier commits with
/// <c>synchronous_commit</c> on, so the batch completes only once the synchronous standbys have flushed its commit, and
/// with it every commit that the read saw, including commits whose own wait was canceled. The read's result is used
/// only once the batch has completed, because rows can reach the client before the commit has waited.
/// </para>
/// <para>
/// A canceled wait completes without an error, so any warning during the batch counts as a barrier that was not
/// confirmed. A terminated backend fails the read. Either way the caller retries, and nothing is used until a barrier
/// is confirmed. On a single server, with no synchronous standby configured, the barrier's commit only flushes the WAL
/// locally, which is enough unless a synchronous standby is required.
/// </para>
/// </remarks>
internal sealed class ReplicationBarrier(
    NpgsqlDataSource dataSource,
    bool requireSynchronousStandby,
    ILogger? logger)
{
    /// <summary>
    /// Runs the read and then the barrier, and returns what <paramref name="readResult"/> took from the read's result
    /// once the barrier is confirmed.
    /// </summary>
    public async Task<T> RunAsync<T>(
        NpgsqlBatchCommand read,
        NpgsqlBatchCommand barrier,
        Func<NpgsqlDataReader, CancellationToken, Task<T>> readResult,
        CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        PostgresNotice? warning = null;
        string? standbyNames = null;
        T result;

        await using (connection.ConfigureAwait(false))
        {
            connection.Notice += OnNotice;

            try
            {
                await using var batch = new NpgsqlBatch(connection);
                batch.BatchCommands.Add(read);
                batch.BatchCommands.Add(barrier);

                var reader = await batch.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

                await using (reader.ConfigureAwait(false))
                {
                    result = await readResult(reader, cancellationToken).ConfigureAwait(false);
                    await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);

                    // The barrier returns a row only if it ran, which it does only if the rows it follows exist.
                    if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        standbyNames = reader.GetString(2);

                    // The batch completes once its commit has, so closing the reader waits for the standbys.
                    await reader.CloseAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                connection.Notice -= OnNotice;
            }
        }

        // A canceled wait completes as if it had been confirmed, so a canceled caller must not use the result.
        cancellationToken.ThrowIfCancellationRequested();

        if (warning is not null)
            throw new ReplicationBarrierException($"The replication barrier was not confirmed: {warning.MessageText}");

        if (standbyNames == string.Empty && requireSynchronousStandby)
        {
            logger?.SynchronousStandbyMissing();

            throw new ReplicationBarrierException(
                "No synchronous standby is configured, but RequireSynchronousStandby is set.");
        }

        return result;

        void OnNotice(object? sender, NpgsqlNoticeEventArgs e)
        {
            if (e.Notice.InvariantSeverity == "WARNING")
                warning ??= e.Notice;
        }
    }
}