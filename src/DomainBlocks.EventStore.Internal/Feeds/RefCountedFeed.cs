namespace DomainBlocks.EventStore.Internal.Feeds;

/// <summary>
/// Connects the feed on the first attach and disconnects it on the last detach. A completed or faulted feed is replaced
/// on the next attach.
/// </summary>
/// <remarks>
/// This class owns each feed that it creates, and disposes it on the last detach or before it replaces it.
/// </remarks>
internal sealed class RefCountedFeed<T, TMark>(Func<IEventLogFeed<T, TMark>> feedFactory) :
    IRefCountedFeed<T, TMark>,
    IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SharedFeed? _current;
    private bool _disposed;

    public async Task<IFeedAttachment<TMark>> AttachAsync(
        IFeedObserver<T> observer,
        string correlationId = "unknown",
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            SharedFeed sharedFeed;
            IDisposable attachment;

            if (_current is null || _current.Feed.Completion.IsCompleted)
            {
                // Observers may still be attached to a completed feed, so their detaching can't be left to dispose it.
                if (_current is { } completed)
                {
                    _current = null;
                    completed.IsDisposed = true;
                    await completed.Feed.DisposeAsync().ConfigureAwait(false);
                }

                (sharedFeed, attachment) = await ConnectAsync(observer, correlationId, cancellationToken)
                    .ConfigureAwait(false);

                _current = sharedFeed;
            }
            else
            {
                sharedFeed = _current;
                attachment = sharedFeed.Feed.Attach(observer, correlationId);
            }

            sharedFeed.RefCount++;

            return new Attachment(
                sharedFeed.Feed.HighWaterMark,
                () => DetachAsync(attachment, sharedFeed));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Disconnects the feed and rejects further attachments. Observers still attached receive an
    /// <see cref="ObjectDisposedException"/> from the feed.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        IEventLogFeed<T, TMark>? feedToDispose;

        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            if (_disposed)
                return;

            _disposed = true;
            feedToDispose = _current?.Feed;

            _current?.IsDisposed = true;

            _current = null;
        }
        finally
        {
            _gate.Release();
        }

        if (feedToDispose is not null)
            await feedToDispose.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<(SharedFeed SharedFeed, IDisposable Attachment)> ConnectAsync(
        IFeedObserver<T> observer,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var feed = feedFactory();

        try
        {
            await feed.ConnectAsync(cancellationToken).ConfigureAwait(false);
            var attachment = feed.Attach(observer, correlationId);

            return (new SharedFeed(feed), attachment);
        }
        catch
        {
            await feed.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task DetachAsync(IDisposable attachment, SharedFeed sharedFeed)
    {
        attachment.Dispose();

        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            sharedFeed.RefCount--;

            if (sharedFeed.RefCount > 0 || sharedFeed.IsDisposed)
                return;

            sharedFeed.IsDisposed = true;

            if (ReferenceEquals(_current, sharedFeed))
                _current = null;

            // This runs within the gate, so the next feed does not connect until this one has stopped and released what
            // it holds on the server. On PostgreSQL that is a replication slot and a walsender, so two feeds at once
            // would hold two of each, counted against max_replication_slots and max_wal_senders.
            await sharedFeed.Feed.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed class SharedFeed(IEventLogFeed<T, TMark> feed)
    {
        public IEventLogFeed<T, TMark> Feed { get; } = feed;
        public int RefCount { get; set; }
        public bool IsDisposed { get; set; }
    }

    private sealed class Attachment(TMark highWaterMark, Func<Task> onDispose) :
        IFeedAttachment<TMark>
    {
        private int _disposed;

        public TMark HighWaterMark { get; } = highWaterMark;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            await onDispose().ConfigureAwait(false);
        }
    }
}