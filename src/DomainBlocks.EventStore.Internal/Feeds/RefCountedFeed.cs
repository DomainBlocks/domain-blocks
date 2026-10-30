namespace DomainBlocks.EventStore.Internal.Feeds;

/// <summary>
/// Connects the feed on the first attach and disconnects it on the last detach. A completed or faulted feed is replaced
/// on the next attach.
/// </summary>
/// <remarks>
/// This class owns each session factory that it creates, and disposes it once the feed that uses it has stopped.
/// </remarks>
/// <param name="createSessionFactory">Creates the session factory of a new feed.</param>
/// <param name="createFeed">Creates a feed over a session factory that it doesn't own.</param>
internal sealed class RefCountedFeed<T, THighWaterMark>(
    Func<IFeedSessionFactory<T, THighWaterMark>> createSessionFactory,
    Func<IFeedSessionFactory<T, THighWaterMark>, IEventLogFeed<T, THighWaterMark>> createFeed) :
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
                // Observers may still be attached to a completed feed, so their detaching can't be left to stop it.
                if (_current is { } completed)
                {
                    _current = null;
                    completed.IsDisposed = true;
                    await StopAsync(completed).ConfigureAwait(false);
                }

                (feedConnection, attachment) = await ConnectAsync(observer, correlationId, cancellationToken)
                    .ConfigureAwait(false);

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
    /// Disconnects the feed and rejects further attachments. Observers still attached receive an
    /// <see cref="ObjectDisposedException"/> from the feed.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        FeedConnection? feedConnectionToStop;

        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            if (_disposed)
                return;

            _disposed = true;
            feedConnectionToStop = _current;

            _current?.IsDisposed = true;

            _current = null;
        }
        finally
        {
            _gate.Release();
        }

        if (feedConnectionToStop is not null)
            await StopAsync(feedConnectionToStop).ConfigureAwait(false);
    }

    private static async Task StopAsync(FeedConnection feedConnection)
    {
        var sessionFactory = feedConnection.SessionFactory;

        await using (sessionFactory.ConfigureAwait(false))
        {
            await feedConnection.Connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<(FeedConnection FeedConnection, IDisposable Attachment)> ConnectAsync(
        IFeedObserver<T> observer,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var sessionFactory = createSessionFactory();
        IFeedConnection<THighWaterMark>? connection = null;

        try
        {
            var feed = createFeed(sessionFactory);
            connection = await feed.ConnectAsync(cancellationToken).ConfigureAwait(false);
            var attachment = feed.Attach(observer, correlationId);

            return (new FeedConnection(feed, connection, sessionFactory), attachment);
        }
        catch
        {
            await using (sessionFactory.ConfigureAwait(false))
            {
                if (connection is not null)
                    await connection.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
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

            // This runs within the gate, so the next connection is not made until this one has stopped and released
            // what it holds on the server. On PostgreSQL that is a replication slot and a walsender, so two
            // connections at once would hold two of each, counted against max_replication_slots and max_wal_senders.
            await StopAsync(feedConnection).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed class FeedConnection(
        IEventLogFeed<T, THighWaterMark> feed,
        IFeedConnection<THighWaterMark> connection,
        IFeedSessionFactory<T, THighWaterMark> sessionFactory)
    {
        public IEventLogFeed<T, THighWaterMark> Feed { get; } = feed;
        public IFeedConnection<THighWaterMark> Connection { get; } = connection;
        public IFeedSessionFactory<T, THighWaterMark> SessionFactory { get; } = sessionFactory;
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