namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

/// <summary>
/// Connects the underlying feed when the first observer is attached and disconnects it when the last observer is
/// detached. A feed whose connection has completed or faulted is replaced on the next attach.
/// </summary>
internal sealed class RefCountedEventLogFeed(Func<IEventLogFeed> feedFactory) :
    IRefCountedEventLogFeed,
    IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private FeedConnection? _current;
    private bool _disposed;

    public async Task<IAsyncDisposable> AttachAsync(
        IEventLogObserver observer,
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

            return new AsyncDisposable(() => DetachAsync(attachment, feedConnection));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Disconnects the current feed, if any, and rejects further attachments. Observers still attached are notified
    /// through their attachment being invalidated by the feed completing.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        IEventLogFeedConnection? connectionToDispose;

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
        IEventLogFeedConnection? connectionToDispose = null;

        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            feedConnection.RefCount--;

            if (feedConnection.RefCount == 0 && !feedConnection.IsDisposed)
            {
                feedConnection.IsDisposed = true;

                if (ReferenceEquals(_current, feedConnection))
                    _current = null;

                connectionToDispose = feedConnection.Connection;
            }
        }
        finally
        {
            _gate.Release();
        }

        if (connectionToDispose is not null)
            await connectionToDispose.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class FeedConnection(IEventLogFeed feed, IEventLogFeedConnection connection)
    {
        public IEventLogFeed Feed { get; } = feed;
        public IEventLogFeedConnection Connection { get; } = connection;
        public int RefCount { get; set; }
        public bool IsDisposed { get; set; }
    }

    private sealed class AsyncDisposable(Func<Task> onDispose) : IAsyncDisposable
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            await onDispose().ConfigureAwait(false);
        }
    }
}
