namespace DomainBlocks.EventStore.Internal.Feeds;

/// <summary>
/// Starts the feed on the first attach and disconnects it on the last detach. A completed or faulted feed is replaced
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
                    await DisposeFeedAsync(completed).ConfigureAwait(false);
                }

                (sharedFeed, attachment) = await StartAsync(observer, correlationId).ConfigureAwait(false);

                _current = sharedFeed;
            }
            else
            {
                sharedFeed = _current;
                attachment = sharedFeed.Feed.Attach(observer, correlationId);
            }

            sharedFeed.RefCount++;

            return new Attachment(sharedFeed, () => DetachAsync(attachment, sharedFeed));
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
        SharedFeed? feedToDispose;

        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            if (_disposed)
                return;

            _disposed = true;
            feedToDispose = _current;

            _current?.IsDisposed = true;

            _current = null;
        }
        finally
        {
            _gate.Release();
        }

        if (feedToDispose is not null)
            await DisposeFeedAsync(feedToDispose).ConfigureAwait(false);
    }

    // The gate isn't held while the feed connects, which could take as long as an outage lasts. Other observers attach,
    // and detach, in the meantime, and each waits for the connection under its own token.
    private async Task<(SharedFeed SharedFeed, IDisposable Attachment)> StartAsync(
        IFeedObserver<T> observer,
        string correlationId)
    {
        var feed = feedFactory();

        try
        {
            var attachment = feed.Attach(observer, correlationId);
            var connection = feed.ConnectAsync();

            return (new SharedFeed(feed, connection), attachment);
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
            await DisposeFeedAsync(sharedFeed).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Disposing stops the feed, which finishes both of its tasks. Observers are told of a failure separately, and the
    // last of them may leave before either task fails, so nothing else might read the tasks' errors. .NET reports an
    // unread error through TaskScheduler.UnobservedTaskException, which would make a subscriber giving up look like a
    // fault that nobody handled.
    private static async ValueTask DisposeFeedAsync(SharedFeed sharedFeed)
    {
        await sharedFeed.Feed.DisposeAsync().ConfigureAwait(false);

        await sharedFeed.Connection.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        await sharedFeed.Feed.Completion.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    private sealed class SharedFeed(IEventLogFeed<T, TMark> feed, Task connection)
    {
        public IEventLogFeed<T, TMark> Feed { get; } = feed;
        public Task Connection { get; } = connection;
        public int RefCount { get; set; }
        public bool IsDisposed { get; set; }
    }

    private sealed class Attachment(SharedFeed sharedFeed, Func<Task> onDispose) :
        IFeedAttachment<TMark>
    {
        private int _disposed;

        public async Task<TMark> WaitForConnectionAsync(CancellationToken cancellationToken)
        {
            await sharedFeed.Connection.WaitAsync(cancellationToken).ConfigureAwait(false);
            return sharedFeed.Feed.HighWaterMark;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            await onDispose().ConfigureAwait(false);
        }
    }
}