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
    /// Creates a sequence that resumes after errors, from the position of the last yielded item.
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
/// Represents a sequence that resumes after an error, from the position of the last yielded item. If the factory
/// creates the sequence from after the position it gets, the caller receives each item once. A resilience pipeline
/// decides which errors to retry, how long to wait, and when to stop.
/// </summary>
internal sealed class ResumableAsyncEnumerable<T, TState, TPosition>(
    Func<TState, TPosition, CancellationToken, IAsyncEnumerable<T>> factory,
    Func<TState, T, TPosition> positionSelector,
    TState state,
    TPosition initialPosition,
    ResiliencePipeline pipeline) :
    IAsyncEnumerable<T>
{
    public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        CreatedSequence? sequence = null;
        var position = initialPosition;

        // The error that the next attempt throws. Each enumeration has its own, because the error stays here if the
        // caller cancels before the pipeline's first attempt.
        var error = new StrongBox<Exception?>();

        try
        {
            while (true)
            {
                bool hasNext;

                if (sequence is null)
                {
                    (sequence, hasNext) = await pipeline
                        .ExecuteAsync(
                            static (s, ct) => s.Self.CreateAsync(s.Position, s.Error, s.CancellationToken, ct),
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
                        hasNext = await sequence.Items.MoveNextAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        // The failed sequence is disposed before the pipeline waits, so it doesn't hold a connection.
                        await sequence.DisposeAsync().ConfigureAwait(false);
                        sequence = null;
                        error.Value = ex;
                        continue;
                    }
                }

                if (!hasNext)
                    yield break;

                var item = sequence.Items.Current;
                yield return item;

                position = positionSelector(state, item);
            }
        }
        finally
        {
            if (sequence is not null)
                await sequence.DisposeAsync().ConfigureAwait(false);
        }
    }

    // Each attempt of a recovery creates the sequence from the position and reads its first item, except the first,
    // which throws the error again.
    private async ValueTask<(CreatedSequence Sequence, bool HasNext)> CreateAsync(
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
        var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CreatedSequence? sequence = null;

        try
        {
            var items = factory(state, position, tokenSource.Token).GetAsyncEnumerator(tokenSource.Token);
            sequence = new CreatedSequence(items, tokenSource);

            bool hasNext;
            var registration = attemptToken.Register(static s => ((CancellationTokenSource)s!).Cancel(), tokenSource);

            await using (registration.ConfigureAwait(false))
                hasNext = await items.MoveNextAsync().ConfigureAwait(false);

            return (sequence, hasNext);
        }
        catch
        {
            if (sequence is not null)
                await sequence.DisposeAsync().ConfigureAwait(false);
            else
                tokenSource.Dispose();

            throw;
        }
    }

    // A created sequence and the token source it was created with, which it disposes after the sequence.
    private sealed record CreatedSequence(IAsyncEnumerator<T> Items, CancellationTokenSource TokenSource) :
        IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await Items.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                TokenSource.Dispose();
            }
        }
    }
}