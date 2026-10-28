using System.Runtime.CompilerServices;

namespace DomainBlocks.EventStore.Internal;

/// <summary>
/// Retries the reads that start a subscription and catch it up when they fail with transient errors, pacing the retries
/// with a <see cref="RetryBackoff"/>. Any other error reaches the caller at once, and so does the last transient error
/// once the backoff stops retrying.
/// </summary>
internal sealed class CatchUpRetry(
    Func<Exception, bool> isTransient,
    RetryBackoff backoff,
    Action<Exception, TimeSpan> onRetry)
{
    /// <summary>
    /// Runs a read, and runs it again after each transient error until it succeeds.
    /// </summary>
    public async Task<T> ReadAsync<T>(Func<CancellationToken, Task<T>> read, CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan delay;

            try
            {
                var result = await read(cancellationToken).ConfigureAwait(false);
                backoff.Reset();

                return result;
            }
            catch (Exception ex) when (IsRetryable(ex, cancellationToken))
            {
                if (!backoff.TryGetNextDelay(out delay))
                    throw;

                onRetry(ex, delay);
            }

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Replays a sequence, and opens it again after each transient error. Each item reaches
    /// <paramref name="onTaken"/> once the caller has taken it and asked for the next one, so a sequence that
    /// <paramref name="open"/> starts after the last item taken misses no item and repeats none.
    /// </summary>
    public async IAsyncEnumerable<T> ReplayAsync<T>(
        Func<CancellationToken, IAsyncEnumerable<T>> open,
        Action<T> onTaken,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan delay;
            var items = open(cancellationToken).GetAsyncEnumerator(cancellationToken);

            await using (items.ConfigureAwait(false))
            {
                while (true)
                {
                    bool hasNext;

                    try
                    {
                        hasNext = await items.MoveNextAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex) when (IsRetryable(ex, cancellationToken))
                    {
                        if (!backoff.TryGetNextDelay(out delay))
                            throw;

                        onRetry(ex, delay);
                        break;
                    }

                    backoff.Reset();

                    if (!hasNext)
                        yield break;

                    var item = items.Current;
                    yield return item;

                    onTaken(item);
                }
            }

            // The failed sequence is disposed before the delay, so it doesn't hold a connection while it waits.
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    // Once the caller has asked to cancel, no error is retried, whatever it is.
    private bool IsRetryable(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && isTransient(exception);
}