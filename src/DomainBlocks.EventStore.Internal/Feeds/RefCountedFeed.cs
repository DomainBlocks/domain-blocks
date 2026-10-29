namespace DomainBlocks.EventStore.Internal.Feeds;

/// <summary>
/// Connects the feed on the first attach and disconnects it on the last detach. A completed or faulted feed is replaced
/// on the next attach.
/// </summary>
internal sealed class RefCountedFeed<T, THighWaterMark>(Func<IFeed<T, THighWaterMark>> feedFactory) :
    IRefCountedFeed<T, THighWaterMark>,
    IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private FeedConnection? _current;
    private bool _disposed;

    public async Task<IFeedAttachment<THighWaterMark>> AttachAsync(
        IFeedObserver<T> observer,
        string correlationId = "unknown",
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            FeedConnection feedConnection;
            IDisposable attachment;

            if (_current is null || _current.Connection.Completion.IsCompleted)
            {
                var feed = feedFactory();
                var connection = await feed.ConnectAsync(cancellationToken).ConfigureAwait(false);

                feedConnection = new FeedConnection(feed, connection);
                attachment = feed.Attach(observer, correlationId);

                _current = feedConnection;
            }
            else
            {
                feedConnection = _current;
                attachment = feedConnection.Feed.Attach(observer, correlationId);
            }

            feedConnection.RefCount++;

            return new Attachment(
                feedConnection.Connection.HighWaterMark,
                () => DetachAsync(attachment, feedConnection));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Disconnects the feed and rejects further attachments. Observers still attached are not notified.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        IFeedConnection<THighWaterMark>? connectionToDispose;

        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            if (_disposed)
                return;

            _disposed = true;
            connectionToDispose = _current?.Connection;

            _current?.IsDisposed = true;

            _current = null;
        }
        finally
        {
            _gate.Release();
        }

        if (connectionToDispose is not null)
            await connectionToDispose.DisposeAsync().ConfigureAwait(false);
    }

    private async Task DetachAsync(IDisposable attachment, FeedConnection feedConnection)
    {
        attachment.Dispose();

        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            feedConnection.RefCount--;

            if (feedConnection.RefCount > 0 || feedConnection.IsDisposed)
                return;

            feedConnection.IsDisposed = true;

            if (ReferenceEquals(_current, feedConnection))
                _current = null;

            // This runs within the gate, so the next connection is not made until this one has handed out its last
            // item and released what it holds. Observers may share what a feed makes of an item, and two connections
            // at once would corrupt it.
            await feedConnection.Connection.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed class FeedConnection(IFeed<T, THighWaterMark> feed, IFeedConnection<THighWaterMark> connection)
    {
        public IFeed<T, THighWaterMark> Feed { get; } = feed;
        public IFeedConnection<THighWaterMark> Connection { get; } = connection;
        public int RefCount { get; set; }
        public bool IsDisposed { get; set; }
    }

    private sealed class Attachment(THighWaterMark highWaterMark, Func<Task> onDispose) :
        IFeedAttachment<THighWaterMark>
    {
        private int _disposed;

        public THighWaterMark HighWaterMark { get; } = highWaterMark;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            await onDispose().ConfigureAwait(false);
        }
    }
}