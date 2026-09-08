using Microsoft.Extensions.Logging;
using Npgsql;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// Commits each append request with its own round trip to the database.
/// </summary>
internal sealed class DirectAppender(NpgsqlDataSource dataSource, SqlNames names, ILogger? logger) : IAppender
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AppendBatchCommand _command = new(dataSource, names);

    public async Task AppendAsync(AppendRequest request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var linkedTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedTimeoutCts.CancelAfter(timeout);

        try
        {
            await _gate.WaitAsync(linkedTimeoutCts.Token).ConfigureAwait(false);

            try
            {
                await _command.ExecuteAsync([request], linkedTimeoutCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger?.AppendBatchFailed(ex, 1);
                request.TryComplete(ex);
            }
            finally
            {
                _gate.Release();
            }

            await request.Completion.WaitAsync(linkedTimeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (linkedTimeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException($"Append operation did not complete within {timeout}.");
        }
    }

    public ValueTask DisposeAsync()
    {
        _command.Dispose();
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}
