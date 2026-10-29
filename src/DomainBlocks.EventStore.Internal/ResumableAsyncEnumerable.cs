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
    /// Creates a sequence that opens again after errors, from the position of the last item taken.
    /// </summary>
    public static ResumableAsyncEnumerable<T, TState, TPosition> Create<T, TState, TPosition>(
        Func<TState, TPosition, CancellationToken, IAsyncEnumerable<T>> factory,
        Func<TState, T, TPosition> positionSelector,
        TState state,
        TPosition initialPosition,
        ResiliencePipeline pipeline)
    {
        return new ResumableAsyncEnumerable<T, TState, TPosition>(
            factory,
            positionSelector,
            state,
            initialPosition,
            pipeline);
    }
}

/// <summary>
/// A sequence that resumes after an error, from the position of the last yielded item. If the factory creates the
/// sequence from after the position it gets, the caller receives each item once. A resilience pipeline decides which
/// errors to retry, how long to wait, and when to stop.
/// </summary>
internal sealed class ResumableAsyncEnumerable<T, TState, TPosition>(
    Func<TState, TPosition, CancellationToken, IAsyncEnumerable<T>> factory,
    Func<TState, T, TPosition> positionSelector,
    TState state,
    TPosition initialPosition,
    ResiliencePipeline pipeline) : IAsyncEnumerable<T>
{
    public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        IAsyncEnumerator<T>? items = null;
        CancellationTokenSource? itemsCancellation = null;
        var position = initialPosition;

        // The error that the next attempt throws. Each enumeration has its own, because the error stays here if the
        // caller cancels before the pipeline's first attempt.
        var error = new StrongBox<Exception?>();

        try
        {
            while (true)
            {
                bool hasNext;

                if (items is null)
                {
                    (items, itemsCancellation, hasNext) = await pipeline
                        .ExecuteAsync(
                            static (s, ct) => s.Self.OpenAsync(s.Position, s.Error, s.CancellationToken, ct),
                            (Self: this, Position: position, Error: error, CancellationToken: cancellationToken),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    // Once the caller has asked to cancel, an error goes out as it is. The pipeline would replace it
                    // with its own OperationCanceledException.
                    try
                    {
                        hasNext = await items.MoveNextAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        // The failed sequence is disposed before the pipeline waits, so it doesn't hold a connection.
                        await items.DisposeAsync().ConfigureAwait(false);
                        itemsCancellation?.Dispose();
                        items = null;
                        itemsCancellation = null;
                        error.Value = ex;
                        continue;
                    }
                }

                if (!hasNext)
                    yield break;

                var item = items.Current;
                yield return item;

                position = positionSelector(state, item);
            }
        }
        finally
        {
            if (items is not null)
                await items.DisposeAsync().ConfigureAwait(false);

            itemsCancellation?.Dispose();
        }
    }

    // Each attempt of a recovery opens the sequence from the position and reads its first item, except the first,
    // which throws the error again.
    private async ValueTask<(IAsyncEnumerator<T> Items, CancellationTokenSource Cancellation, bool HasNext)> OpenAsync(
        TPosition position,
        StrongBox<Exception?> error,
        CancellationToken cancellationToken,
        CancellationToken attemptToken)
    {
        if (error.Value is { } ex)
        {
            error.Value = null;
            ExceptionDispatchInfo.Throw(ex);
        }

        // The sequence outlives the attempt, so it takes a token linked to the caller's, which the attempt's token
        // cancels only while the attempt runs.
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var items = factory(state, position, cancellation.Token).GetAsyncEnumerator(cancellation.Token);

        try
        {
            bool hasNext;
            var registration = attemptToken.Register(static s => ((CancellationTokenSource)s!).Cancel(), cancellation);

            await using (registration.ConfigureAwait(false))
                hasNext = await items.MoveNextAsync().ConfigureAwait(false);

            return (items, cancellation, hasNext);
        }
        catch
        {
            await items.DisposeAsync().ConfigureAwait(false);
            cancellation.Dispose();
            throw;
        }
    }
}