using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Polly;

namespace DomainBlocks.EventStore.Internal;

/// <summary>
/// Provides methods for creating <see cref="ResumableAsyncEnumerable{T,TState,TPosition}"/> sequences.
/// </summary>
internal static class ResumableAsyncEnumerable
{
    /// <summary>
    /// Creates an async sequence that resumes after errors, from the position of the last yielded item.
    /// </summary>
    public static ResumableAsyncEnumerable<T, TState, TPosition> Create<T, TState, TPosition>(
        Func<TState, TPosition, CancellationToken, IAsyncEnumerable<T>> factory,
        Func<TState, T, TPosition> positionSelector,
        TState state,
        TPosition startPosition,
        ResiliencePipeline pipeline)
    {
        return new ResumableAsyncEnumerable<T, TState, TPosition>(
            factory,
            positionSelector,
            state,
            startPosition,
            pipeline);
    }
}

/// <summary>
/// Represents an async sequence that resumes after an error, from the position of the last yielded item. If the factory
/// creates the sequence from after the position it gets, the caller receives each item once. A resilience pipeline
/// decides which errors to retry, how long to wait, and when to stop.
/// </summary>
internal sealed class ResumableAsyncEnumerable<T, TState, TPosition>(
    Func<TState, TPosition, CancellationToken, IAsyncEnumerable<T>> factory,
    Func<TState, T, TPosition> positionSelector,
    TState state,
    TPosition startPosition,
    ResiliencePipeline pipeline) :
    IAsyncEnumerable<T>
{
    public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        IAsyncEnumerator<T>? enumerator = null;
        var position = startPosition;

        // The error that the next attempt throws. Each enumeration has its own, because the error stays here if the
        // caller cancels before the pipeline's first attempt.
        var error = new StrongBox<Exception?>();

        try
        {
            while (true)
            {
                if (enumerator is null)
                {
                    enumerator = await pipeline
                        .ExecuteAsync(
                            static (s, ct) => s.Self.CreateAsync(s.Position, s.Error, s.CallerToken, ct),
                            (Self: this, Position: position, Error: error, CallerToken: cancellationToken),
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (enumerator is null)
                        yield break;
                }
                else
                {
                    // Once the caller has asked to cancel, an error goes out as it is. The pipeline would replace it
                    // with its own OperationCanceledException.
                    try
                    {
                        if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                            yield break;
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        // The failed sequence is disposed before the pipeline waits, so it doesn't hold a connection.
                        await enumerator.DisposeAsync().ConfigureAwait(false);
                        enumerator = null;
                        error.Value = ex;
                        continue;
                    }
                }

                var item = enumerator.Current;
                yield return item;

                position = positionSelector(state, item);
            }
        }
        finally
        {
            if (enumerator is not null)
                await enumerator.DisposeAsync().ConfigureAwait(false);
        }
    }

    // Each attempt of a recovery creates the sequence from the position and reads its first item, except the first,
    // which throws the error again. A sequence with no item is disposed, and the attempt returns null.
    private async ValueTask<IAsyncEnumerator<T>?> CreateAsync(
        TPosition position,
        StrongBox<Exception?> error,
        CancellationToken callerToken,
        CancellationToken attemptToken)
    {
        if (error.Value is { } ex)
        {
            error.Value = null;
            ExceptionDispatchInfo.Throw(ex);
        }

        // The sequence outlives the attempt, so it takes a token linked to the caller's, which the attempt's token
        // cancels only while the attempt runs.
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        IAsyncEnumerator<T>? enumerator = null;

        try
        {
            var registration = attemptToken.Register(static s => ((CancellationTokenSource)s!).Cancel(), linkedCts);
            bool hasNext;

            await using (registration.ConfigureAwait(false))
            {
                var items = factory(state, position, linkedCts.Token).GetAsyncEnumerator(linkedCts.Token);
                enumerator = new AsyncEnumeratorWrapper(items, linkedCts);
                hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
            }

            if (hasNext)
                return enumerator;

            await enumerator.DisposeAsync().ConfigureAwait(false);
            return null;
        }
        catch
        {
            if (enumerator is not null)
                await enumerator.DisposeAsync().ConfigureAwait(false);
            else
                linkedCts.Dispose();

            throw;
        }
    }

    private sealed class AsyncEnumeratorWrapper(IAsyncEnumerator<T> inner, IDisposable resource) : IAsyncEnumerator<T>
    {
        public T Current => inner.Current;

        public ValueTask<bool> MoveNextAsync() => inner.MoveNextAsync();

        public async ValueTask DisposeAsync()
        {
            try
            {
                await inner.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                resource.Dispose();
            }
        }
    }
}