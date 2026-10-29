using System.Collections.Immutable;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.Internal.Feeds;

/// <summary>
/// Holds a feed's observers and notifies them in turn. An observer that throws while receiving an item or a reset is
/// detached. Once the feed completes or faults, no observer can attach.
/// </summary>
internal sealed class FeedObservers<T>(ILogger? logger, string feedId)
{
    private State _state = new([]);

    public IDisposable Attach(IFeedObserver<T> observer, string observerId)
    {
        var attachment = new Attachment(observer, observerId, Detach);

        TryUpdate(
            static (current, attachment) => current.Completion switch
            {
                { Error: { } error } => throw new InvalidOperationException("Cannot attach to a faulted feed.", error),
                { Error: null } => throw new InvalidOperationException("Cannot attach to a completed feed."),
                _ => new State(current.Attachments.Add(attachment))
            },
            attachment,
            out var next);

        logger?.ObserverAttached(feedId, observerId, next.Attachments.Length);
        return attachment;
    }

    public async ValueTask NotifyNextAsync(T item, CancellationToken cancellationToken)
    {
        foreach (var attachment in Volatile.Read(ref _state).Attachments)
        {
            try
            {
                await attachment.Observer.OnNextAsync(item, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception observerException)
            {
                logger?.ObserverOnNextFailed(observerException, feedId, attachment.ObserverId);
                attachment.Dispose();
            }
        }
    }

    public async ValueTask NotifyResetAsync(CancellationToken cancellationToken)
    {
        var attachments = Volatile.Read(ref _state).Attachments;
        logger?.FeedReset(feedId, attachments.Length);

        foreach (var attachment in attachments)
        {
            try
            {
                await attachment.Observer.OnResetAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception observerException)
            {
                logger?.ObserverOnResetFailed(observerException, feedId, attachment.ObserverId);
                attachment.Dispose();
            }
        }
    }

    public async ValueTask NotifyErrorAsync(Exception error, CancellationToken cancellationToken)
    {
        if (!TryUpdate(
                static (current, error) => current.Completion is null
                    ? new State(current.Attachments, new Completion(error))
                    : null,
                error,
                out var faulted))
        {
            return;
        }

        foreach (var attachment in faulted.Attachments)
        {
            try
            {
                await attachment.Observer.OnErrorAsync(error, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception observerException)
            {
                logger?.ObserverOnErrorFailed(observerException, feedId, attachment.ObserverId);
            }
        }
    }

    public void SetComplete()
    {
        TryUpdate(
            static (current, completion) => current.Completion is null
                ? new State(current.Attachments, completion)
                : null,
            new Completion(),
            out _);
    }

    private void Detach(Attachment attachment)
    {
        TryUpdate(
            static (current, attachment) => new State(current.Attachments.Remove(attachment), current.Completion),
            attachment,
            out var next);

        logger?.ObserverDetached(feedId, attachment.ObserverId, next.Attachments.Length);
    }

    private bool TryUpdate<TArg>(Func<State, TArg, State?> updater, TArg argument, out State result)
    {
        while (true)
        {
            var current = Volatile.Read(ref _state);
            var next = updater(current, argument);

            if (next is null)
            {
                result = current;
                return false;
            }

            if (!ReferenceEquals(Interlocked.CompareExchange(ref _state, next, current), current))
                continue;

            result = next;
            return true;
        }
    }

    private sealed class State(ImmutableArray<Attachment> attachments, Completion? completion = null)
    {
        public ImmutableArray<Attachment> Attachments { get; } = attachments;
        public Completion? Completion { get; } = completion;
    }

    private sealed class Attachment(IFeedObserver<T> observer, string observerId, Action<Attachment> onDispose) :
        IDisposable
    {
        private int _disposed;

        public IFeedObserver<T> Observer { get; } = observer;
        public string ObserverId { get; } = observerId;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                onDispose(this);
        }
    }

    private sealed class Completion(Exception? error = null)
    {
        public Exception? Error { get; } = error;
    }
}