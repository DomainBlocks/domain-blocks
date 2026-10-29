using DomainBlocks.EventStore.Internal.Feeds;

namespace DomainBlocks.EventStore.Internal.Tests.Unit.Feeds;

/// <summary>
/// A session factory that opens sessions with a delegate and holds nothing across them. It treats an
/// <see cref="IOException"/> as transient, unless the test says otherwise.
/// </summary>
internal sealed class DelegateSessionFactory<T>(
    Func<CancellationToken, Task<IFeedSession<T, long>>> open,
    Func<Exception, bool>? isTransient = null) :
    IFeedSessionFactory<T, long>
{
    private readonly Func<Exception, bool> _isTransient = isTransient ?? (static ex => ex is IOException);

    public int DisposeCount { get; private set; }

    public Task<IFeedSession<T, long>> OpenAsync(CancellationToken cancellationToken) => open(cancellationToken);

    public bool IsTransient(Exception exception) => _isTransient(exception);

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return ValueTask.CompletedTask;
    }
}