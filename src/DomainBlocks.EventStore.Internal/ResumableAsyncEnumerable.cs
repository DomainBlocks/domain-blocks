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
        TState state,
        TPosition initialPosition,
        Func<TState, TPosition, CancellationToken, IAsyncEnumerable<T>> factory,
        Func<TState, T, TPosition> positionSelector,
        ResiliencePipeline pipeline)
    {
        return new ResumableAsyncEnumerable<T, TState, TPosition>(
            state,
            initialPosition,
            factory,
            positionSelector,
            pipeline);
    }
}

/// <summary>
/// A sequence that opens again after an error, from the position of the last item taken. If the factory opens the
/// sequence after the position it gets, the caller receives each item once. A resilience pipeline decides which errors
/// to retry, how long to wait, and when to stop.
/// </summary>
/// <remarks>
/// <para>
/// Items are read without the pipeline. After an error, the failed sequence is disposed and one execution of the
/// pipeline recovers: its first attempt throws the error again, so the pipeline handles it like any failed attempt,
/// and each later attempt opens the sequence and reads its first item. An attempt succeeds only when the sequence makes
/// progress, with an item or its end, because opening a lazy sequence can't fail. The next error starts a new
/// execution. The first open is an execution too, with no error to throw.
/// </para>
/// <para>
/// The factory gets the initial position for the first open, and for each later open the position of the last item
/// taken, or the initial position if the caller has taken none. Each enumeration starts from the initial position.
/// </para>
/// <para>
/// An attempt opens the sequence with the token that the pipeline gives it, and the sequence keeps that token after
/// the attempt ends. The pipeline must therefore give each attempt the caller's token, as a retry strategy does, and
/// not one of its own, as a timeout strategy does.
/// </para>
/// </remarks>
internal sealed class ResumableAsyncEnumerable<T, TState, TPosition>(
    TState state,
    TPosition initialPosition,
    Func<TState, TPosition, CancellationToken, IAsyncEnumerable<T>> factory,
    Func<TState, T, TPosition> positionSelector,
    ResiliencePipeline pipeline) : IAsyncEnumerable<T>
{
    public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        IAsyncEnumerator<T>? items = null;
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
                    (items, hasNext) = await pipeline
                        .ExecuteAsync(
                            static (s, ct) => s.Source.OpenAsync(s.Position, s.Error, ct),
                            (Source: this, Position: position, Error: error),
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
                        items = null;
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
        }
    }

    // Each attempt of a recovery opens the sequence from the position and reads its first item, except the first,
    // which throws the error again.
    private async ValueTask<(IAsyncEnumerator<T> Items, bool HasNext)> OpenAsync(
        TPosition position,
        StrongBox<Exception?> error,
        CancellationToken cancellationToken)
    {
        if (error.Value is { } ex)
        {
            error.Value = null;
            ExceptionDispatchInfo.Throw(ex);
        }

        var items = factory(state, position, cancellationToken).GetAsyncEnumerator(cancellationToken);

        try
        {
            return (items, await items.MoveNextAsync().ConfigureAwait(false));
        }
        catch
        {
            await items.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}