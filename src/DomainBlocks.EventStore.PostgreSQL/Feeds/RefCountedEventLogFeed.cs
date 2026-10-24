namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

/// <summary>
/// Connects the feed on the first attach and disconnects it on the last detach. A completed or faulted feed is replaced
/// on the next attach.
/// </summary>
internal sealed class RefCountedEventLogFeed<T>(Func<IEventLogFeed<T>> feedFactory) :
    IRefCountedEventLogFeed<T>,
    IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private FeedConnection? _current;
    private bool _disposed;

    public async Task<IAsyncDisposable> AttachAsync(
        IEventLogObserver<T> observer,
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
    /// Disconnects the feed and rejects further attachments. Observers still attached are not notified.
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

    private sealed class FeedConnection(IEventLogFeed<T> feed, IEventLogFeedConnection connection)
    {
        public IEventLogFeed<T> Feed { get; } = feed;
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